using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NewsFactory.Collection.Html;

namespace NewsFactory.Collection.AutoSetup;

public sealed record ListCandidate(HtmlSourceConfig Config, int Items, double Score);

public sealed record ListItem(string Url, string Title);

/// <summary>
/// Guesses the news list of a page without LLM: looks for runs of sibling elements that each hold a link to an article
/// on the same site with a headline-like text, builds CSS selectors for them and ranks the candidates.
/// </summary>
public static class ListPageAnalyzer
{
    private const int MinItems = 3;
    private const int MinTitleLength = 15;
    private const double MinShare = 0.6;
    private static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");
    private static readonly string[] Chrome = ["nav", "header", "footer", "aside"];

    public static IReadOnlyList<ListCandidate> Suggest(string html, Uri pageUrl)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var host = Host(pageUrl);
        var seen = new HashSet<string>();
        var result = new List<ListCandidate>();

        foreach (var parent in doc.All.Where(e => e.Children.Length >= MinItems))
        {
            var groups = parent.Children.GroupBy(Signature).Where(g => g.Count() >= MinItems).Select(g => (Selector: g.Key, Members: g.ToList()))
                .Concat([(Selector: "", Members: parent.Children.ToList())]);
            foreach (var (childSig, members) in groups)
            {
                var parentSel = SelectorFor(parent);
                if (parentSel is null) continue;
                var itemSelector = childSig.Length > 0 ? $"{parentSel} > {childSig}" : $"{parentSel} > {members[0].LocalName}";
                if (!seen.Add(itemSelector)) continue;
                if (Evaluate(doc, itemSelector, pageUrl, host) is { } candidate) result.Add(candidate);
            }
        }
        return result.OrderByDescending(c => c.Score).Take(5).ToList();
    }

    /// <summary>Applies a config to the list page only (no article downloads), the way HtmlCollector reads the list.</summary>
    public static IReadOnlyList<ListItem> ListItems(string html, Uri pageUrl, HtmlSourceConfig config)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var items = new List<ListItem>();
        IHtmlCollection<IElement> elements;
        try { elements = doc.QuerySelectorAll(config.ItemSelector); } catch (DomException) { return items; }
        foreach (var element in elements)
        {
            IElement? link;
            try { link = element.QuerySelector(config.LinkSelector); } catch (DomException) { return items; }
            if (link?.GetAttribute("href") is not { } href || !Uri.TryCreate(pageUrl, href, out var url)) continue;
            var titleEl = config.TitleSelector is null ? link : element.QuerySelector(config.TitleSelector);
            items.Add(new ListItem(url.ToString(), Clean(titleEl?.TextContent)));
        }
        return items;
    }

    private static ListCandidate? Evaluate(IDocument doc, string itemSelector, Uri pageUrl, string host)
    {
        IHtmlCollection<IElement> items;
        try { items = doc.QuerySelectorAll(itemSelector); } catch (DomException) { return null; }
        if (items.Length < MinItems) return null;
        if (items.Any(i => i.Ancestors<IElement>().Any(a => Chrome.Contains(a.LocalName)))) return null;

        var linkSelector = PickLinkSelector(items, pageUrl, host);
        if (linkSelector is null) return null;
        var titles = items.Select(i => TitleLink(i.QuerySelector(linkSelector), pageUrl, host)).Where(t => t is not null).Select(t => t!.Value).ToList();
        if (titles.Count < MinItems || titles.Count < items.Length * MinShare) return null;
        if (titles.Select(t => t.Url).Distinct().Count() < MinItems) return null;

        var dateSelector = PickDateSelector(items);
        // News headlines are long, news links share a section path (/news/…, /text/…) and news items carry a date;
        // site menus and document lists have short, scattered links and no dates.
        var avgLength = titles.Average(t => Math.Min(t.Title.Length, 90));
        var lengthFactor = Math.Pow(avgLength / 60.0, 2);
        var sections = titles.GroupBy(t => FirstSegment(t.Url)).Max(g => g.Count()) / (double)titles.Count;
        var articleLike = titles.Count(t => LooksLikeArticle(t.Url)) / (double)titles.Count;
        var nested = items.Count(i => i.QuerySelector("ul, ol") is not null) / (double)items.Length;
        var score = Math.Sqrt(titles.Count) * lengthFactor * sections * (0.5 + articleLike) * (dateSelector is null ? 1 : 3)
                    * (titles.Count / (double)items.Length) * (1 - nested * 0.8);
        var config = new HtmlSourceConfig(pageUrl.ToString(), itemSelector, linkSelector, DateSelector: dateSelector);
        return new ListCandidate(config, titles.Count, score);
    }

    /// <summary>Simplest selector that points, inside most items, at the headline link.</summary>
    private static string? PickLinkSelector(IHtmlCollection<IElement> items, Uri pageUrl, string host)
    {
        var options = new List<string> { "a" };
        foreach (var item in items.Take(5))
        {
            var best = item.QuerySelectorAll("a[href]").Select(a => (A: a, Title: TitleLink(a, pageUrl, host)))
                .Where(x => x.Title is not null).OrderByDescending(x => x.Title!.Value.Title.Length).Select(x => x.A).FirstOrDefault();
            if (best is null) continue;
            foreach (var cls in best.ClassList) options.Add("a." + Css(cls));
            if (best.ParentElement is { } parent && parent != item)
            {
                if (parent.ClassList.Length > 0) options.Add($"{parent.LocalName}.{Css(parent.ClassList[0])} a");
                if (parent.LocalName is "h1" or "h2" or "h3" or "h4" or "h5") options.Add($"{parent.LocalName} a");
            }
        }
        return options.Distinct().FirstOrDefault(sel =>
        {
            var ok = 0;
            foreach (var item in items)
            {
                IElement? link;
                try { link = item.QuerySelector(sel); } catch (DomException) { return false; }
                if (TitleLink(link, pageUrl, host) is not null) ok++;
            }
            return ok >= MinItems && ok >= items.Length * MinShare;
        });
    }

    private static string? PickDateSelector(IHtmlCollection<IElement> items)
    {
        var options = new List<string>();
        foreach (var item in items.Take(5))
        {
            foreach (var el in item.QuerySelectorAll("*").Where(e => e.Children.Length == 0 || e.LocalName == "time"))
            {
                if (!IsDate(el)) continue;
                // Stable hooks first: <time> and data-*date* attributes survive site redesigns better than generated classes.
                if (el.LocalName == "time") options.Add("time");
                var dataAttr = el.Attributes.FirstOrDefault(a => a.Name.StartsWith("data-") && a.Name.Contains("date"));
                if (dataAttr is not null) options.Add($"[{dataAttr.Name}]");
                foreach (var cls in el.ClassList) options.Add($"{el.LocalName}.{Css(cls)}");
            }
        }
        return options.Distinct().FirstOrDefault(sel =>
            items.Count(i => i.QuerySelector(sel) is { } d && IsDate(d)) >= items.Length * MinShare);
    }

    private static bool IsDate(IElement el)
    {
        if (el.LocalName == "time" && DateTimeOffset.TryParse(el.GetAttribute("datetime"), out _)) return true;
        var text = Clean(el.TextContent);
        return text.Length is > 5 and <= 40 && DateParsing.TryParse(text, null, Samara) is not null;
    }

    private static (string Url, string Title)? TitleLink(IElement? a, Uri pageUrl, string host)
    {
        if (a?.GetAttribute("href") is not { } href || href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!Uri.TryCreate(pageUrl, href, out var url) || url.Scheme is not ("http" or "https")) return null;
        if (Host(url) != host && !Host(url).EndsWith("." + host) && !host.EndsWith("." + Host(url))) return null;
        if (url.AbsolutePath.TrimEnd('/') == pageUrl.AbsolutePath.TrimEnd('/')) return null;
        // The collector takes the headline from the link text, so the title attribute does not count.
        var title = Clean(a.TextContent);
        return title.Length >= MinTitleLength ? (url.ToString(), title) : null;
    }

    private static string FirstSegment(string url) =>
        new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    private static bool LooksLikeArticle(string url)
    {
        var path = new Uri(url).AbsolutePath;
        return path.Count(c => c == '/') >= 2 || path.Any(char.IsDigit) || path.EndsWith(".html") || path.EndsWith(".htm") || path.Length > 30;
    }

    private static string Signature(IElement e) =>
        e.LocalName + string.Concat(e.ClassList.OrderBy(c => c, StringComparer.Ordinal).Select(c => "." + Css(c)));

    /// <summary>CSS selector for a list container: id if it looks hand-written, else tag + classes, else a short path.</summary>
    private static string? SelectorFor(IElement e, int depth = 0)
    {
        if (e.LocalName is "html" or "body") return e.LocalName;
        if (!string.IsNullOrEmpty(e.Id) && e.Id.Count(char.IsDigit) < 3) return "#" + Css(e.Id);
        if (e.ClassList.Length > 0) return e.LocalName + string.Concat(e.ClassList.Select(c => "." + Css(c)));
        if (depth >= 3 || e.ParentElement is null) return null;
        var parent = SelectorFor(e.ParentElement, depth + 1);
        return parent is null ? null : $"{parent} > {e.LocalName}";
    }

    private static string Css(string ident) => string.Concat(ident.Select(ch =>
        char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch.ToString() : "\\" + ch));

    private static string Host(Uri u) => u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..].ToLowerInvariant() : u.Host.ToLowerInvariant();

    private static string Clean(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
