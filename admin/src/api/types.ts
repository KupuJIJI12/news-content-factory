// Mirrors of the admin API DTOs (src/NewsFactory.Api/Admin/**). Enums are serialized as strings.

export type StoryStatus =
  | 'New' | 'Processing' | 'LowInterest' | 'ReadyForReview' | 'Scheduled' | 'Published' | 'Rejected'
  | 'ProcessingFailed' | 'PublishFailed'
export type PublicationStatus = 'Scheduled' | 'Sending' | 'Published' | 'Failed' | 'Unknown' | 'Cancelled'
export type QueueTab = 'Review' | 'LowInterest' | 'Scheduled' | 'Published' | 'Rejected' | 'Errors'
export type SourceType = 'Rss' | 'Html' | 'Vk'
export type SourceHealthStatus = 'Ok' | 'Failing' | 'Silent'
export type Platform = 'Max'
export type LlmTask = 'Relevance' | 'Clustering' | 'Scoring' | 'Rewrite' | 'FactCheck'
export type LlmProviderKind = 'OpenAiCompatible' | 'GigaChat'
export type FactCheckIssueSource = 'Deterministic' | 'Llm'

export interface Me { id: number; login: string; timeZone: string }

export interface ChannelToday { channelId: number; name: string; published: number; scheduled: number; limit: number }
export interface ErrorBreakdown {
  sourcesFailing: number; sourcesSilent: number; storiesProcessingFailed: number; storiesPublishFailed: number
  rawItemsFailed24h: number; budgetExceeded: boolean; total: number
}
export interface Counters { onReview: number; withUpdates: number; errors: ErrorBreakdown; today: ChannelToday[] }

export interface StoryListItem {
  id: number; status: StoryStatus; score: number | null; title: string; sourcesCount: number; hasUpdates: boolean
  updatedAt: string; scheduledAt: string | null
}
export interface StoryListPage { items: StoryListItem[]; total: number }

export interface SourceImage { url: string; allowed: boolean }
export interface StorySourceDto {
  rawItemId: number; sourceName: string; url: string; title: string; text: string; publishedAt: string | null; images: SourceImage[]
}
export interface FactCheckIssue { claim: string; problem: string; source: FactCheckIssueSource }
export interface DraftDto {
  id: number; version: number; title: string; body: string; hashtags: string[]; byHuman: boolean; regenerationHint: string | null
  factCheckIssues: FactCheckIssue[]; createdAt: string; isCurrent: boolean
}
export interface StoryImageDto { id: number; url: string; credit: string | null; isAllowed: boolean; uploaded: boolean }
export interface PublicationDto {
  id: number; channelId: number; channelName: string; status: PublicationStatus; scheduledAt: string; publishedAt: string | null
  lastError: string | null; overrideLimit: boolean
}
export interface StoryDetail {
  id: number; status: StoryStatus; score: number | null; scoreReason: string | null; summary: string; hasUpdates: boolean
  lastError: string | null; sources: StorySourceDto[]; currentDraft: DraftDto | null; versions: DraftDto[]
  images: StoryImageDto[]; publications: PublicationDto[]
}

export interface DraftInput { title: string; body: string; hashtags: string[] }
export interface PublishInput { at?: string; imageId?: number; overrideLimit: boolean }
export interface PublishResponse { publicationIds: number[] }
export interface PreviewInput { title: string; body: string; hashtags: string[]; imageCredit?: string | null; cityId?: number }
export interface PreviewResult {
  channelId: number; channelName: string; platform: Platform; text: string; length: number; max: number; tooLong: boolean
  channelConfigured: boolean
}

export interface SourceDto {
  id: number; name: string; type: SourceType; url: string; config: string | null; pollIntervalMinutes: number
  expectedFrequencyHours: number | null; photosAllowedWithCredit: boolean; photoCredit: string | null; isEnabled: boolean
  isDedicated: boolean; lastRunAt: string | null; lastSuccessAt: string | null; lastItemAt: string | null
  consecutiveFailures: number; lastError: string | null; health: SourceHealthStatus
}
export interface SourceInput {
  name: string; type: SourceType; url: string; config: string | null; pollIntervalMinutes: number
  expectedFrequencyHours: number | null; photosAllowedWithCredit: boolean; photoCredit: string | null; isEnabled: boolean
  isDedicated: boolean
}
export interface SourceTestItem { title: string; url: string; publishedAt: string | null; textPreview: string; imageCount: number }
export interface SourceTestResult { ok: boolean; error: string | null; items: SourceTestItem[] }
export interface HtmlSourceConfig {
  listUrl: string; itemSelector: string; linkSelector: string; titleSelector?: string | null; dateSelector?: string | null
  dateFormat?: string | null; bodySelector?: string | null; imageSelector?: string | null
}

export interface CitySettings { name: string; keywords: string[]; scoreThreshold: number; timeZone: string }
export interface ChannelSettings { id: number; name: string; platform: Platform; externalChatId: string; dailyLimit: number; isEnabled: boolean }
export interface Secrets { maxBotToken: boolean; vkServiceToken: boolean; gigachatAuthKey: boolean; yandexApiKey: boolean }
export interface LlmProvider {
  id: number; name: string; kind: LlmProviderKind; baseUrl: string; apiKeyEnvVar: string; models: string[]
  headers: Record<string, string>; supportsJsonSchema: boolean; authUrl: string | null; authScope: string | null
  apiKeyConfigured: boolean
}
export type LlmProviderInput = Omit<LlmProvider, 'id' | 'apiKeyConfigured'>
export interface LlmTaskSetting { task: LlmTask; providerId: number; providerName: string; model: string; temperature: number }
export interface PromptVersion { id: number; version: number; isActive: boolean; createdAt: string; text: string }
export interface PromptVariable { name: string; description: string; required: boolean }
export interface PromptTask { task: LlmTask; description: string; variables: PromptVariable[]; versions: PromptVersion[] }
export interface Usage { today: number; month: number; dailyBudget: number }
