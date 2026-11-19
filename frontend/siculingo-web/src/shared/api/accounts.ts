import createClient from 'openapi-fetch'
import type { components, paths } from './schema'

export type Account = components['schemas']['AccountResponse']
export type Recovery = components['schemas']['RecoveryResponse']
export type LearningPath = components['schemas']['LearningPathResponse']
export type LessonAttempt = components['schemas']['LessonAttemptResponse']
export type StudentExercise = components['schemas']['StudentExerciseDto']
export type SubmittedAnswer = components['schemas']['SubmittedExerciseAnswer']
export type ExerciseSubmission = components['schemas']['ExerciseSubmissionResponse']
export type LessonCompletion = components['schemas']['LessonCompletionResponse']
export type GamificationSummary = components['schemas']['GamificationSummaryResponse']
export type HeartsStatus = components['schemas']['HeartsStatusDto']
export type MissionsOverview = components['schemas']['MissionsOverviewResponse']
export type MissionProgressItem = components['schemas']['MissionStatusItemDto']
export type AchievementsCatalog = components['schemas']['AchievementsCatalogResponse']
export type PrivacySettings = components['schemas']['PrivacySettingsResponse']
export type SocialOverview = components['schemas']['SocialOverviewResponse']
export type FriendSummary = components['schemas']['FriendSummaryDto']
export type FriendRequestItem = components['schemas']['FriendRequestItemDto']
export type BlockedUser = components['schemas']['BlockedUserDto']
export type UserSearchItem = components['schemas']['UserSearchItemDto']
export type PublicProfile = components['schemas']['PublicProfileResponse']
export type LeaderboardData = components['schemas']['LeaderboardResponse']
export type LeaderboardEntry = components['schemas']['LeaderboardEntryDto']
export type AdminOverview = components['schemas']['AdminOverviewResponse']
export type EditorialCatalog = components['schemas']['EditorialCatalogResponse']
export type EditorialLessonSummary = components['schemas']['EditorialLessonSummaryDto']
export type EditorialLessonDetail = components['schemas']['EditorialLessonDetailResponse']
export type LinguisticLexemeItem = components['schemas']['LinguisticLexemeDto']
export type AdminReportItem = components['schemas']['AdminReportDto']
export type ManagedUserItem = components['schemas']['ManagedUserDto']
export type AdminAuditItem = components['schemas']['AdminAuditEntryDto']
export type LegalNotice = components['schemas']['LegalNoticeResponse']
export type ProductMetrics = components['schemas']['ProductMetricsResponse']
export type ExerciseAccuracyMetric = components['schemas']['ExerciseAccuracyMetricDto']
type RegisterPayload = components['schemas']['RegisterAccountRequest']
type Credentials = components['schemas']['CredentialsRequest']
type Problem = components['schemas']['ProblemDetails']

const client = createClient<paths>({ credentials: 'same-origin' })

export class ApiError extends Error {
  readonly status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

function fail(response: Response, problem?: Problem): never {
  throw new ApiError(response.status, problem?.detail || problem?.title || `Richiesta fallita (HTTP ${response.status}).`)
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Operazione non riuscita. Riprova.'
}

async function csrf() {
  const { data, error, response } = await client.GET('/api/v1/auth/csrf', { cache: 'no-store' })
  if (!data) fail(response, error)
  return { 'X-CSRF-TOKEN': data.token }
}

export async function currentAccount(signal: AbortSignal): Promise<Account | null> {
  const { data, error, response } = await client.GET('/api/v1/auth/me', { signal, cache: 'no-store' })
  if (response.status === 401) return null
  if (!data) fail(response, error)
  return data
}

export async function register(body: RegisterPayload): Promise<Recovery> {
  const { data, error, response } = await client.POST('/api/v1/auth/register', { body, headers: await csrf() })
  if (!data) fail(response, error)
  return data
}

export async function login(body: Credentials): Promise<Account> {
  const { data, error, response } = await client.POST('/api/v1/auth/login', { body, headers: await csrf() })
  if (!data) fail(response, error)
  return data
}

export async function recover(body: components['schemas']['RecoveryRequest']): Promise<Recovery> {
  const { data, error, response } = await client.POST('/api/v1/auth/recover', { body, headers: await csrf() })
  if (!data) fail(response, error)
  return data
}

export async function logout(allDevices: boolean): Promise<void> {
  const headers = await csrf()
  const { error, response } = allDevices
    ? await client.POST('/api/v1/auth/logout-all', { headers })
    : await client.POST('/api/v1/auth/logout', { headers })
  if (!response.ok) fail(response, error)
}

export async function learningPath(signal: AbortSignal): Promise<LearningPath> {
  const { data, error, response } = await client.GET('/api/v1/learning/path', { signal, cache: 'no-store' })
  if (!data) fail(response, error)
  return data
}

export async function updateProfile(displayName: string): Promise<Account> {
  const { data, error, response } = await client.POST('/api/v1/auth/profile', {
    body: { displayName }, headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function changePassword(body: components['schemas']['ChangePasswordRequest']): Promise<void> {
  const { error, response } = await client.POST('/api/v1/auth/password', { body, headers: await csrf() })
  if (!response.ok) fail(response, error)
}

export async function deleteAccount(currentPassword: string): Promise<void> {
  const { error, response } = await client.POST('/api/v1/auth/delete', {
    body: { currentPassword },
    headers: await csrf(),
  })
  if (!response.ok) fail(response, error)
}

export async function legalNotice(signal?: AbortSignal): Promise<LegalNotice> {
  const { data, error, response } = await client.GET('/api/v1/system/legal', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function adminProductMetrics(windowDays = 30, signal?: AbortSignal): Promise<ProductMetrics> {
  const { data, error, response } = await client.GET('/api/v1/admin/product-metrics', {
    params: { query: { windowDays } },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function startLessonAttempt(
  lessonId: string,
  replaceActive = false,
  practiceMode = false,
): Promise<LessonAttempt> {
  const { data, error, response } = await client.POST('/api/v1/learning/lessons/{lessonId}/attempts', {
    params: { path: { lessonId } },
    body: { replaceActive, practiceMode },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function startPracticeSession(replaceActive = false): Promise<LessonAttempt> {
  const { data, error, response } = await client.POST('/api/v1/learning/practice/start', {
    body: { replaceActive },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function getLessonAttempt(attemptId: string, signal?: AbortSignal): Promise<LessonAttempt> {
  const { data, error, response } = await client.GET('/api/v1/learning/attempts/{attemptId}', {
    params: { path: { attemptId } },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function submitExerciseAnswer(
  attemptId: string,
  exerciseId: string,
  answer: SubmittedAnswer,
): Promise<ExerciseSubmission> {
  const { data, error, response } = await client.POST('/api/v1/learning/attempts/{attemptId}/answers', {
    params: { path: { attemptId } },
    body: { exerciseId, answer },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function completeLessonAttempt(attemptId: string): Promise<LessonCompletion> {
  const { data, error, response } = await client.POST('/api/v1/learning/attempts/{attemptId}/complete', {
    params: { path: { attemptId } },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function abandonLessonAttempt(attemptId: string): Promise<void> {
  const { error, response } = await client.POST('/api/v1/learning/attempts/{attemptId}/abandon', {
    params: { path: { attemptId } },
    headers: await csrf(),
  })
  if (!response.ok) fail(response, error)
}

export async function gamificationSummary(signal: AbortSignal): Promise<GamificationSummary> {
  const { data, error, response } = await client.GET('/api/v1/gamification/summary', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function userAchievements(signal: AbortSignal): Promise<AchievementsCatalog> {
  const { data, error, response } = await client.GET('/api/v1/gamification/achievements', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function reportExerciseIssue(
  body: components['schemas']['CreateExerciseReportRequest'],
): Promise<components['schemas']['ReportCreatedResponse']> {
  const { data, error, response } = await client.POST('/api/v1/reports/exercises', {
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function adminOverview(signal?: AbortSignal): Promise<AdminOverview> {
  const { data, error, response } = await client.GET('/api/v1/admin/overview', { signal, cache: 'no-store' })
  if (!data) fail(response, error)
  return data
}

export async function adminCatalog(signal?: AbortSignal): Promise<EditorialCatalog> {
  const { data, error, response } = await client.GET('/api/v1/admin/catalog', { signal, cache: 'no-store' })
  if (!data) fail(response, error)
  return data
}

export async function adminLessonDetail(lessonId: string, signal?: AbortSignal): Promise<EditorialLessonDetail> {
  const { data, error, response } = await client.GET('/api/v1/admin/lessons/{lessonId}', {
    params: { path: { lessonId } },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function createLessonDraft(
  unitId: string,
  body: components['schemas']['CreateLessonDraftRequest'],
): Promise<EditorialLessonSummary> {
  const { data, error, response } = await client.POST('/api/v1/admin/units/{unitId}/lessons', {
    params: { path: { unitId } },
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function saveLessonDraft(
  lessonId: string,
  body: components['schemas']['SaveLessonDraftRequest'],
): Promise<EditorialLessonDetail> {
  const { data, error, response } = await client.POST('/api/v1/admin/lessons/{lessonId}/draft', {
    params: { path: { lessonId } },
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function submitLessonForReview(lessonId: string, note: string | null): Promise<EditorialLessonSummary> {
  const { data, error, response } = await client.POST('/api/v1/admin/lessons/{lessonId}/submit-review', {
    params: { path: { lessonId } },
    body: { note },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function reviewLessonDraft(
  lessonId: string,
  body: components['schemas']['ReviewLessonRequest'],
): Promise<EditorialLessonSummary> {
  const { data, error, response } = await client.POST('/api/v1/admin/lessons/{lessonId}/review', {
    params: { path: { lessonId } },
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function publishApprovedLesson(lessonId: string): Promise<EditorialLessonSummary> {
  const { data, error, response } = await client.POST('/api/v1/admin/lessons/{lessonId}/publish', {
    params: { path: { lessonId } },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function unpublishLesson(lessonId: string): Promise<EditorialLessonSummary> {
  const { data, error, response } = await client.POST('/api/v1/admin/lessons/{lessonId}/unpublish', {
    params: { path: { lessonId } },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function adminLexemes(signal?: AbortSignal): Promise<LinguisticLexemeItem[]> {
  const { data, error, response } = await client.GET('/api/v1/admin/lexemes', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data.items
}

export async function upsertLexeme(
  body: components['schemas']['UpsertLinguisticLexemeRequest'],
): Promise<LinguisticLexemeItem> {
  const { data, error, response } = await client.POST('/api/v1/admin/lexemes', {
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function reviewLexeme(
  lexemeId: string,
  body: components['schemas']['ReviewLexemeRequest'],
): Promise<LinguisticLexemeItem> {
  const { data, error, response } = await client.POST('/api/v1/admin/lexemes/{lexemeId}/review', {
    params: { path: { lexemeId } },
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function adminReports(status?: string, signal?: AbortSignal): Promise<AdminReportItem[]> {
  const { data, error, response } = await client.GET('/api/v1/admin/reports', {
    params: { query: status ? { status } : {} },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data.items
}

export async function resolveAdminReport(
  reportId: string,
  body: components['schemas']['ResolveUserReportRequest'],
): Promise<AdminReportItem> {
  const { data, error, response } = await client.POST('/api/v1/admin/reports/{reportId}/resolve', {
    params: { path: { reportId } },
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function adminUsers(search?: string, signal?: AbortSignal): Promise<ManagedUserItem[]> {
  const { data, error, response } = await client.GET('/api/v1/admin/users', {
    params: { query: search ? { search } : {} },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data.items
}

export async function setUserSuspension(
  targetUserId: string,
  body: components['schemas']['SetUserSuspensionRequest'],
): Promise<ManagedUserItem> {
  const { data, error, response } = await client.POST('/api/v1/admin/users/{targetUserId}/suspension', {
    params: { path: { targetUserId } },
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function updateUserRole(
  targetUserId: string,
  body: components['schemas']['UpdateUserRoleRequest'],
): Promise<ManagedUserItem> {
  const { data, error, response } = await client.POST('/api/v1/admin/users/{targetUserId}/roles', {
    params: { path: { targetUserId } },
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function adminAuditLog(signal?: AbortSignal): Promise<AdminAuditItem[]> {
  const { data, error, response } = await client.GET('/api/v1/admin/audit', { signal, cache: 'no-store' })
  if (!data) fail(response, error)
  return data.items
}

export async function userHearts(signal?: AbortSignal): Promise<HeartsStatus> {
  const { data, error, response } = await client.GET('/api/v1/gamification/hearts', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function userMissions(signal?: AbortSignal): Promise<MissionsOverview> {
  const { data, error, response } = await client.GET('/api/v1/gamification/missions', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function socialPrivacy(signal?: AbortSignal): Promise<PrivacySettings> {
  const { data, error, response } = await client.GET('/api/v1/social/privacy', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function updateSocialPrivacy(
  body: components['schemas']['UpdatePrivacySettingsRequest'],
): Promise<PrivacySettings> {
  const { data, error, response } = await client.POST('/api/v1/social/privacy', {
    body,
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function socialOverview(signal?: AbortSignal): Promise<SocialOverview> {
  const { data, error, response } = await client.GET('/api/v1/social/overview', {
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function searchSocialUsers(
  query: string,
  signal?: AbortSignal,
): Promise<components['schemas']['UserSearchResponse']> {
  const { data, error, response } = await client.GET('/api/v1/social/users/search', {
    params: { query: { q: query } },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function publicProfile(username: string, signal?: AbortSignal): Promise<PublicProfile> {
  const { data, error, response } = await client.GET('/api/v1/social/profiles/{username}', {
    params: { path: { username } },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}

export async function sendFriendRequest(
  targetUserId: string,
): Promise<components['schemas']['FriendActionResponse']> {
  const { data, error, response } = await client.POST('/api/v1/social/friends/requests', {
    body: { targetUserId },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function acceptFriendRequest(
  friendshipId: string,
): Promise<components['schemas']['FriendActionResponse']> {
  const { data, error, response } = await client.POST('/api/v1/social/friends/requests/{friendshipId}/accept', {
    params: { path: { friendshipId } },
    headers: await csrf(),
  })
  if (!data) fail(response, error)
  return data
}

export async function rejectFriendRequest(friendshipId: string): Promise<void> {
  const { error, response } = await client.POST('/api/v1/social/friends/requests/{friendshipId}/reject', {
    params: { path: { friendshipId } },
    headers: await csrf(),
  })
  if (!response.ok) fail(response, error)
}

export async function removeFriend(friendUserId: string): Promise<void> {
  const { error, response } = await client.POST('/api/v1/social/friends/{friendUserId}/remove', {
    params: { path: { friendUserId } },
    headers: await csrf(),
  })
  if (!response.ok) fail(response, error)
}

export async function blockSocialUser(targetUserId: string): Promise<void> {
  const { error, response } = await client.POST('/api/v1/social/blocks', {
    body: { targetUserId },
    headers: await csrf(),
  })
  if (!response.ok) fail(response, error)
}

export async function unblockSocialUser(blockedUserId: string): Promise<void> {
  const { error, response } = await client.POST('/api/v1/social/blocks/{blockedUserId}/unblock', {
    params: { path: { blockedUserId } },
    headers: await csrf(),
  })
  if (!response.ok) fail(response, error)
}

export async function weeklyLeaderboard(
  scope: 'league' | 'friends',
  signal?: AbortSignal,
): Promise<LeaderboardData> {
  const { data, error, response } = await client.GET('/api/v1/social/leaderboard', {
    params: { query: { scope } },
    signal,
    cache: 'no-store',
  })
  if (!data) fail(response, error)
  return data
}


