using NewsFactory.Domain;

namespace NewsFactory.Publishing;

public sealed class DailyLimitExceededException(DailyLimitCheck check)
    : Exception($"Дневной лимит канала исчерпан: опубликовано {check.Published}, запланировано {check.Scheduled}, лимит {check.Limit}")
{
    public DailyLimitCheck Check { get; } = check;
}
