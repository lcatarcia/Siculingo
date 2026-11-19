import { useCallback, useEffect, useState, type FormEvent } from 'react'
import {
  adminAuditLog,
  adminCatalog,
  adminLessonDetail,
  adminLexemes,
  adminOverview,
  adminProductMetrics,
  adminReports,
  adminUsers,
  ApiError,
  createLessonDraft,
  errorMessage,
  publishApprovedLesson,
  resolveAdminReport,
  reviewLessonDraft,
  reviewLexeme,
  saveLessonDraft,
  setUserSuspension,
  submitLessonForReview,
  unpublishLesson,
  updateUserRole,
  upsertLexeme,
  type Account,
  type AdminAuditItem,
  type AdminOverview,
  type AdminReportItem,
  type EditorialCatalog,
  type EditorialLessonDetail,
  type LinguisticLexemeItem,
  type ManagedUserItem,
  type ProductMetrics,
} from '../../shared/api/accounts'

type Props = {
  account: Account
  onExpired: () => void
}

type AdminTab = 'catalog' | 'lexicon' | 'reports' | 'users' | 'metrics' | 'audit'

function toNumber(value: number | string): number {
  return typeof value === 'number' ? value : Number(value)
}

function stateLabel(state: string): string {
  switch (state) {
    case 'approved':
      return 'Approvata'
    case 'inReview':
      return 'In revisione'
    default:
      return 'Bozza'
  }
}

export default function AdminConsolePage({ account, onExpired }: Props) {
  const isAdmin = account.roles.includes('Admin')
  const [tab, setTab] = useState<AdminTab>('catalog')
  const [overview, setOverview] = useState<AdminOverview | null>(null)
  const [catalog, setCatalog] = useState<EditorialCatalog | null>(null)
  const [selectedLessonId, setSelectedLessonId] = useState<string | null>(null)
  const [lessonDetail, setLessonDetail] = useState<EditorialLessonDetail | null>(null)
  const [draftText, setDraftText] = useState('')
  const [variantsText, setVariantsText] = useState('')
  const [sourceText, setSourceText] = useState('')
  const [reviewNoteText, setReviewNoteText] = useState('')
  const [creatingUnitId, setCreatingUnitId] = useState<string | null>(null)

  const [lexemes, setLexemes] = useState<LinguisticLexemeItem[]>([])
  const [lexemeSearch, setLexemeSearch] = useState('')
  const [editingLexeme, setEditingLexeme] = useState<LinguisticLexemeItem | null>(null)

  const [reports, setReports] = useState<AdminReportItem[]>([])
  const [reportFilter, setReportFilter] = useState<string>('open')
  const [users, setUsers] = useState<ManagedUserItem[]>([])
  const [userSearch, setUserSearch] = useState('')
  const [auditItems, setAuditItems] = useState<AdminAuditItem[]>([])
  const [productMetrics, setProductMetrics] = useState<ProductMetrics | null>(null)
  const [metricsWindowDays, setMetricsWindowDays] = useState<number>(30)

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const handleApiError = useCallback(
    (err: unknown) => {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setError(errorMessage(err))
    },
    [onExpired],
  )

  const refreshCore = useCallback(async () => {
    const [ov, cat] = await Promise.all([adminOverview(), adminCatalog()])
    setOverview(ov)
    setCatalog(cat)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([adminOverview(controller.signal), adminCatalog(controller.signal)])
      .then(([ov, cat]) => {
        if (!controller.signal.aborted) {
          setOverview(ov)
          setCatalog(cat)
        }
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) handleApiError(err)
      })
    return () => controller.abort()
  }, [handleApiError])

  useEffect(() => {
    if (tab !== 'lexicon') return
    const controller = new AbortController()
    adminLexemes(controller.signal)
      .then(items => {
        if (!controller.signal.aborted) setLexemes(items)
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) handleApiError(err)
      })
    return () => controller.abort()
  }, [tab, handleApiError])

  useEffect(() => {
    if (tab !== 'reports' || !isAdmin) return
    const controller = new AbortController()
    adminReports(reportFilter === 'all' ? undefined : reportFilter, controller.signal)
      .then(items => {
        if (!controller.signal.aborted) setReports(items)
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) handleApiError(err)
      })
    return () => controller.abort()
  }, [tab, reportFilter, isAdmin, handleApiError])

  useEffect(() => {
    if (tab !== 'users' || !isAdmin) return
    const controller = new AbortController()
    adminUsers(userSearch || undefined, controller.signal)
      .then(items => {
        if (!controller.signal.aborted) setUsers(items)
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) handleApiError(err)
      })
    return () => controller.abort()
  }, [tab, userSearch, isAdmin, handleApiError])

  useEffect(() => {
    if (tab !== 'audit' || !isAdmin) return
    const controller = new AbortController()
    adminAuditLog(controller.signal)
      .then(items => {
        if (!controller.signal.aborted) setAuditItems(items)
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) handleApiError(err)
      })
    return () => controller.abort()
  }, [tab, isAdmin, handleApiError])

  useEffect(() => {
    if (tab !== 'metrics' || !isAdmin) return
    const controller = new AbortController()
    adminProductMetrics(metricsWindowDays, controller.signal)
      .then(data => {
        if (!controller.signal.aborted) setProductMetrics(data)
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) handleApiError(err)
      })
    return () => controller.abort()
  }, [tab, metricsWindowDays, isAdmin, handleApiError])

  async function selectLesson(lessonId: string) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const detail = await adminLessonDetail(lessonId)
      setSelectedLessonId(lessonId)
      setLessonDetail(detail)
      setDraftText(detail.draftJson)
      setVariantsText(detail.metadata.orthographicVariants ?? '')
      setSourceText(detail.metadata.sourceReference ?? '')
      setReviewNoteText(detail.metadata.reviewerNotes ?? '')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleSaveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selectedLessonId) return
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const updated = await saveLessonDraft(selectedLessonId, {
        draftJson: draftText,
        orthographicVariants: variantsText || null,
        sourceReference: sourceText || null,
      })
      setLessonDetail(updated)
      setDraftText(updated.draftJson)
      await refreshCore()
      setNotice(
        updated.metadata.isPublished
          ? 'Bozza aggiornata. La versione pubblicata e i tentativi attivi restano invariati finché la bozza non viene riapprovata e pubblicata.'
          : 'Bozza lezione salvata.',
      )
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleSubmitReview() {
    if (!selectedLessonId) return
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await submitLessonForReview(selectedLessonId, reviewNoteText || null)
      const detail = await adminLessonDetail(selectedLessonId)
      setLessonDetail(detail)
      await refreshCore()
      setNotice('Bozza inviata a revisione linguistica.')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleReviewDecision(approved: boolean) {
    if (!selectedLessonId) return
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await reviewLessonDraft(selectedLessonId, {
        approved,
        reviewerNotes:
          reviewNoteText.trim() ||
          (approved ? 'Revisione linguistica catanese verificata e approvata.' : 'Richiesta revisione ortografica/contestuale.'),
      })
      const detail = await adminLessonDetail(selectedLessonId)
      setLessonDetail(detail)
      setReviewNoteText(detail.metadata.reviewerNotes ?? '')
      await refreshCore()
      setNotice(approved ? 'Bozza approvata linguisticamente. Ora può essere pubblicata.' : 'Bozza rimandata in lavorazione.')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handlePublishToggle(publish: boolean) {
    if (!selectedLessonId) return
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      if (publish) await publishApprovedLesson(selectedLessonId)
      else await unpublishLesson(selectedLessonId)
      const detail = await adminLessonDetail(selectedLessonId)
      setLessonDetail(detail)
      await refreshCore()
      setNotice(publish ? 'Lezione pubblicata sul sentiero studenti.' : 'Lezione ritirata dal sentiero pubblico.')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleCreateLesson(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!creatingUnitId) return
    const form = new FormData(event.currentTarget)
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const createdSummary = await createLessonDraft(creatingUnitId, {
        title: String(form.get('title') ?? ''),
        objective: String(form.get('objective') ?? ''),
        reviewNote: String(form.get('reviewNote') ?? ''),
        orthographicVariants: String(form.get('orthographicVariants') ?? '') || null,
        sourceReference: String(form.get('sourceReference') ?? '') || null,
      })
      setCreatingUnitId(null)
      const [created] = await Promise.all([adminLessonDetail(createdSummary.id), refreshCore()])
      setSelectedLessonId(created.metadata.id)
      setLessonDetail(created)
      setDraftText(created.draftJson)
      setVariantsText(created.metadata.orthographicVariants ?? '')
      setSourceText(created.metadata.sourceReference ?? '')
      setReviewNoteText(created.metadata.reviewerNotes ?? '')
      setNotice('Nuova bozza lezione creata (non pubblicata).')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleSaveLexeme(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await upsertLexeme({
        id: editingLexeme?.id ?? null,
        headword: String(form.get('headword') ?? ''),
        meaningIt: String(form.get('meaningIt') ?? ''),
        usageContext: String(form.get('usageContext') ?? ''),
        register: String(form.get('register') ?? 'colloquiale'),
        orthographicVariants: String(form.get('orthographicVariants') ?? '') || null,
        sourceReference: String(form.get('sourceReference') ?? '') || null,
        submitForReview: true,
      })
      setEditingLexeme(null)
      event.currentTarget.reset()
      const [items] = await Promise.all([adminLexemes(), refreshCore()])
      setLexemes(items)
      setNotice('Voce del lessico catanese salvata e inviata a revisione.')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleLexemeReview(lexemeId: string, approved: boolean) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await reviewLexeme(lexemeId, {
        approved,
        reviewerNotes: approved ? 'Forma e registro verificati.' : 'Da rivedere.',
      })
      const [items] = await Promise.all([adminLexemes(), refreshCore()])
      setLexemes(items)
      setNotice(approved ? 'Voce lessicale approvata.' : 'Voce lessicale riportata in bozza.')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleResolveReport(reportId: string, status: 'resolved' | 'dismissed', note: string) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await resolveAdminReport(reportId, { status, resolutionNote: note || null })
      const [items] = await Promise.all([
        adminReports(reportFilter === 'all' ? undefined : reportFilter),
        refreshCore(),
      ])
      setReports(items)
      setNotice(status === 'resolved' ? 'Segnalazione risolta.' : 'Segnalazione archiviata.')
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleToggleRole(user: ManagedUserItem, role: 'Editor' | 'Admin') {
    const hasRole = user.roles.includes(role)
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await updateUserRole(user.id, { role, grant: !hasRole })
      setUsers(await adminUsers(userSearch || undefined))
      setNotice(`Ruolo ${role} ${!hasRole ? 'assegnato a' : 'rimosso da'} @${user.username}.`)
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  async function handleToggleSuspension(user: ManagedUserItem, reasonInput?: string) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await setUserSuspension(user.id, {
        suspend: !user.isSuspended,
        reason: !user.isSuspended ? reasonInput || 'Sospensione amministrativa' : null,
      })
      const [items] = await Promise.all([adminUsers(userSearch || undefined), refreshCore()])
      setUsers(items)
      setNotice(!user.isSuspended ? `Account @${user.username} sospeso e sessioni revocate.` : `Account @${user.username} riattivato.`)
    } catch (err: unknown) {
      handleApiError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="learning-page admin-page" aria-labelledby="admin-console-heading">
      <div className="learning-heading trophy-hero">
        <div>
          <span className="account-eyebrow">
            {isAdmin ? 'REDAZIONE E AMMINISTRAZIONE · ADMIN' : 'REDAZIONE LINGUISTICA · EDITOR'}
          </span>
          <h1 id="admin-console-heading">Presidio editoriale e moderazione</h1>
          <p>
            Nessuna lezione può andare online senza revisione linguistica approvata. Le modifiche alle bozze non alterano mai i
            tentativi in corso.
          </p>
        </div>
        <div className="trophy-counter-badge">
          <img src="/assets/badges/shield.png" alt="" width="34" height="34" />
          <div>
            <strong>{account.roles.join(' · ')}</strong>
            <span>@{account.username}</span>
          </div>
        </div>
      </div>

      {overview && (
        <div className="admin-metrics-grid" aria-label="Indicatori redazione e piattaforma">
          <div className="lesson-metric-card">
            <span>LEZIONI PUBBLICATE</span>
            <strong>{toNumber(overview.publishedLessons)}</strong>
          </div>
          <div className="lesson-metric-card">
            <span>IN REVISIONE / BOZZA</span>
            <strong>
              {toNumber(overview.inReviewLessons)} / {toNumber(overview.draftLessons)}
            </strong>
          </div>
          <div className="lesson-metric-card">
            <span>VOCI LESSICO (APPR.)</span>
            <strong>
              {toNumber(overview.approvedLexemes)} / {toNumber(overview.totalLexemes)}
            </strong>
          </div>
          <div className="lesson-metric-card">
            <span>UTENTI / SEGNALAZIONI</span>
            <strong>
              {toNumber(overview.totalUsers)} / {toNumber(overview.openReports)}
            </strong>
          </div>
        </div>
      )}

      <div className="trophy-filters admin-tabs" role="tablist" aria-label="Aree console">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'catalog'}
          className={`trophy-filter-pill ${tab === 'catalog' ? 'active' : ''}`}
          onClick={() => setTab('catalog')}
        >
          Catalogo e bozze
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'lexicon'}
          className={`trophy-filter-pill ${tab === 'lexicon' ? 'active' : ''}`}
          onClick={() => setTab('lexicon')}
        >
          Lessico catanese
        </button>
        {isAdmin && (
          <>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'reports'}
              className={`trophy-filter-pill ${tab === 'reports' ? 'active' : ''}`}
              onClick={() => setTab('reports')}
            >
              Segnalazioni {overview && toNumber(overview.openReports) > 0 ? `(${toNumber(overview.openReports)})` : ''}
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'users'}
              className={`trophy-filter-pill ${tab === 'users' ? 'active' : ''}`}
              onClick={() => setTab('users')}
            >
              Utenti e ruoli
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'metrics'}
              className={`trophy-filter-pill ${tab === 'metrics' ? 'active' : ''}`}
              onClick={() => setTab('metrics')}
            >
              Metriche beta
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'audit'}
              className={`trophy-filter-pill ${tab === 'audit' ? 'active' : ''}`}
              onClick={() => setTab('audit')}
            >
              Registro audit
            </button>
          </>
        )}
      </div>

      {error && (
        <div className="account-error" role="alert">
          {error}
        </div>
      )}
      {notice && (
        <div className="account-notice" role="status">
          {notice}
        </div>
      )}

      {tab === 'catalog' && (
        <div className="admin-split-grid">
          <div className="learning-panel admin-list-panel">
            <h2>Struttura corsi e lezioni</h2>
            {!catalog ? (
              <p role="status">Caricamento catalogo editoriale…</p>
            ) : (
              catalog.courses.map(course => (
                <div key={course.id} className="admin-course-block">
                  <div className="admin-course-head">
                    <strong>{course.name}</strong>
                    <span className={`admin-pill ${course.isPublished ? 'published' : 'draft'}`}>
                      {course.isPublished ? 'Corso pubblicato' : 'Bozza'}
                    </span>
                  </div>
                  {course.sections.map(section => (
                    <div key={section.id} className="admin-section-block">
                      <h3>{section.title}</h3>
                      {section.units.map(unit => (
                        <div key={unit.id} className="admin-unit-block">
                          <div className="learning-unit-header">
                            <h4>{unit.title}</h4>
                            <button
                              type="button"
                              className="account-secondary admin-mini-btn"
                              disabled={busy}
                              onClick={() => setCreatingUnitId(creatingUnitId === unit.id ? null : unit.id)}
                            >
                              + Nuova bozza
                            </button>
                          </div>

                          {creatingUnitId === unit.id && (
                            <form className="admin-inline-form" onSubmit={e => void handleCreateLesson(e)}>
                              <label>
                                Titolo bozza lezione
                                <input name="title" required minLength={3} maxLength={200} placeholder="Es. Al mercato della Pescheria" />
                              </label>
                              <label>
                                Obiettivo didattico
                                <input name="objective" required maxLength={300} placeholder="Chiedere prezzi e quantità in catanese" />
                              </label>
                              <label>
                                Nota per revisione linguistica
                                <input name="reviewNote" required maxLength={500} placeholder="Bozza in attesa di verifica parlante nativo" />
                              </label>
                              <label>
                                Varianti ortografiche / fonte (opzionale)
                                <input name="orthographicVariants" maxLength={500} placeholder="Es. pisciria / pisciaria" />
                              </label>
                              <input type="hidden" name="sourceReference" value="Redazione interna Siculingo" />
                              <div className="admin-action-row">
                                <button type="submit" className="account-primary admin-inline-primary" disabled={busy}>
                                  Crea bozza
                                </button>
                                <button type="button" className="account-secondary" onClick={() => setCreatingUnitId(null)}>
                                  Annulla
                                </button>
                              </div>
                            </form>
                          )}

                          <ul className="admin-lesson-list">
                            {unit.lessons.map(lesson => (
                              <li key={lesson.id}>
                                <button
                                  type="button"
                                  className={`admin-lesson-row ${selectedLessonId === lesson.id ? 'selected' : ''}`}
                                  onClick={() => void selectLesson(lesson.id)}
                                >
                                  <div>
                                    <strong>
                                      {toNumber(lesson.sortOrder)}. {lesson.draftTitle}
                                    </strong>
                                    <small>
                                      {toNumber(lesson.exerciseCount)} esercizi · {stateLabel(lesson.editorialState)}
                                      {lesson.hasUnpublishedChanges ? ' · Modifiche non pubblicate' : ''}
                                    </small>
                                  </div>
                                  <span className={`admin-pill ${lesson.isPublished ? 'published' : lesson.editorialState}`}>
                                    {lesson.isPublished ? 'Online' : stateLabel(lesson.editorialState)}
                                  </span>
                                </button>
                              </li>
                            ))}
                          </ul>
                        </div>
                      ))}
                    </div>
                  ))}
                </div>
              ))
            )}
          </div>

          <div className="learning-panel admin-editor-panel">
            {!lessonDetail ? (
              <div className="admin-empty-selection">
                <img src="/assets/mascot/ficodi-study.png" alt="" width="76" height="88" />
                <h3>Seleziona una lezione</h3>
                <p>Scegli una lezione a sinistra per ispezionare esercizi, metadati linguistici, stato di revisione e pubblicazione.</p>
              </div>
            ) : (
              <form className="admin-editor-form" onSubmit={e => void handleSaveDraft(e)}>
                <div className="admin-editor-header">
                  <div>
                    <span className="account-eyebrow">
                      STATO REVISIONE: {stateLabel(lessonDetail.metadata.editorialState).toUpperCase()}
                    </span>
                    <h2>{lessonDetail.metadata.draftTitle}</h2>
                    <p>
                      {lessonDetail.metadata.isPublished
                        ? `Pubblicata (${lessonDetail.metadata.publishedOrCurrentTitle})`
                        : 'Non ancora pubblicata sul sentiero studenti'}
                      {lessonDetail.metadata.approvedByName ? ` · Approvata da ${lessonDetail.metadata.approvedByName}` : ''}
                    </p>
                  </div>
                </div>

                <label>
                  Varianti ortografiche attestate
                  <input
                    value={variantsText}
                    onChange={e => setVariantsText(e.target.value)}
                    maxLength={500}
                    placeholder="Es. mbare / 'mbare; chi dici / cchi dici"
                  />
                </label>

                <label>
                  Fonte / riferimento linguistico
                  <input
                    value={sourceText}
                    onChange={e => setSourceText(e.target.value)}
                    maxLength={500}
                    placeholder="Es. Uso vivo catanese verificato con revisore madrelingua"
                  />
                </label>

                <label>
                  Bozza strutturata esercizi (JSON validato lato server)
                  <textarea
                    rows={13}
                    value={draftText}
                    onChange={e => setDraftText(e.target.value)}
                    spellCheck={false}
                    className="admin-json-textarea"
                  />
                </label>

                <label>
                  Note di revisione linguistica
                  <input
                    value={reviewNoteText}
                    onChange={e => setReviewNoteText(e.target.value)}
                    maxLength={2000}
                    placeholder="Osservazioni su registro, naturalezza catanese o distrattori"
                  />
                </label>

                <div className="admin-action-row">
                  <button type="submit" className="account-primary admin-inline-primary" disabled={busy}>
                    Salva bozza
                  </button>
                  {lessonDetail.metadata.editorialState !== 'approved' && (
                    <button
                      type="button"
                      className="account-secondary"
                      disabled={busy}
                      onClick={() => void handleSubmitReview()}
                    >
                      Invia a revisione
                    </button>
                  )}
                </div>

                {isAdmin && (
                  <div className="admin-reviewer-box">
                    <span className="account-eyebrow">PRESIDIO REVISORE / ADMIN</span>
                    <div className="admin-action-row">
                      <button
                        type="button"
                        className="account-secondary"
                        disabled={busy}
                        onClick={() => void handleReviewDecision(true)}
                      >
                        Approva revisione linguistica
                      </button>
                      <button
                        type="button"
                        className="account-secondary"
                        disabled={busy}
                        onClick={() => void handleReviewDecision(false)}
                      >
                        Richiedi modifiche
                      </button>
                      <button
                        type="button"
                        className="account-primary admin-inline-primary"
                        disabled={busy || lessonDetail.metadata.editorialState !== 'approved'}
                        onClick={() => void handlePublishToggle(true)}
                      >
                        {lessonDetail.metadata.isPublished ? 'Pubblica aggiornamento approvato' : 'Pubblica lezione'}
                      </button>
                      {lessonDetail.metadata.isPublished && (
                        <button
                          type="button"
                          className="account-text-button lesson-abandon-btn admin-unpublish-btn"
                          disabled={busy}
                          onClick={() => void handlePublishToggle(false)}
                        >
                          Ritira pubblicazione
                        </button>
                      )}
                    </div>
                  </div>
                )}
              </form>
            )}
          </div>
        </div>
      )}

      {tab === 'lexicon' && (
        <div className="admin-split-grid">
          <div className="learning-panel">
            <div className="learning-unit-header">
              <h2>Archivio lessicale catanese</h2>
              <input
                type="search"
                value={lexemeSearch}
                onChange={e => setLexemeSearch(e.target.value)}
                placeholder="Cerca lemma o significato…"
                aria-label="Cerca nel lessico catanese"
                className="admin-search-input"
              />
            </div>
            {(() => {
              const needle = lexemeSearch.trim().toLowerCase()
              const filteredLexemes = needle
                ? lexemes.filter(
                    item =>
                      item.headword.toLowerCase().includes(needle) ||
                      item.meaningIt.toLowerCase().includes(needle) ||
                      (item.orthographicVariants ?? '').toLowerCase().includes(needle),
                  )
                : lexemes
              return filteredLexemes.length === 0 ? (
                <p className="admin-muted-copy">Nessuna voce registrata con questo filtro. Aggiungi un lemma dal modulo accanto.</p>
              ) : (
                <ul className="admin-card-list">
                  {filteredLexemes.map(item => (
                  <li key={item.id} className="admin-item-card">
                    <div className="learning-unit-header">
                      <div>
                        <strong>{item.headword}</strong> — <span>{item.meaningIt}</span>
                      </div>
                      <span className={`admin-pill ${item.reviewState === 'approved' ? 'published' : 'inReview'}`}>
                        {stateLabel(item.reviewState)} · {item.register}
                      </span>
                    </div>
                    <p>{item.usageContext}</p>
                    {(item.orthographicVariants || item.sourceReference) && (
                      <small>
                        {item.orthographicVariants ? `Varianti: ${item.orthographicVariants}` : ''}
                        {item.orthographicVariants && item.sourceReference ? ' · ' : ''}
                        {item.sourceReference ? `Fonte: ${item.sourceReference}` : ''}
                      </small>
                    )}
                    <div className="admin-action-row">
                      <button type="button" className="account-secondary admin-mini-btn" onClick={() => setEditingLexeme(item)}>
                        Modifica
                      </button>
                      {isAdmin && item.reviewState !== 'approved' && (
                        <button
                          type="button"
                          className="account-secondary admin-mini-btn"
                          disabled={busy}
                          onClick={() => void handleLexemeReview(item.id, true)}
                        >
                          Approva lemma
                        </button>
                      )}
                    </div>
                  </li>
                  ))}
                </ul>
              )
            })()}
          </div>

          <div className="learning-panel">
            <h2>{editingLexeme ? `Modifica «${editingLexeme.headword}»` : 'Nuova voce lessicale'}</h2>
            <form key={editingLexeme?.id ?? 'new'} className="admin-editor-form" onSubmit={e => void handleSaveLexeme(e)}>
              <label>
                Forma catanese (lemma o espressione)
                <input name="headword" required maxLength={120} defaultValue={editingLexeme?.headword ?? ''} placeholder="Es. Mbare" />
              </label>
              <label>
                Significato in italiano
                <input name="meaningIt" required maxLength={300} defaultValue={editingLexeme?.meaningIt ?? ''} placeholder="Es. Amico, compare (appellativo affettuoso)" />
              </label>
              <label>
                Contesto d’uso reale
                <textarea
                  name="usageContext"
                  rows={3}
                  required
                  maxLength={600}
                  defaultValue={editingLexeme?.usageContext ?? ''}
                  placeholder="Usato nel parlato informale catanese per salutare o interpellare con calore."
                />
              </label>
              <label>
                Registro comunicativo
                <select name="register" defaultValue={editingLexeme?.register ?? 'colloquiale'}>
                  <option value="colloquiale">Colloquiale / espressivo</option>
                  <option value="informale">Informale / quotidiano</option>
                  <option value="neutro">Neutro</option>
                  <option value="cortese">Cortese / rispettoso</option>
                </select>
              </label>
              <label>
                Varianti ortografiche (opzionale)
                <input name="orthographicVariants" maxLength={500} defaultValue={editingLexeme?.orthographicVariants ?? ''} placeholder="Es. 'mbare, cumpà" />
              </label>
              <label>
                Fonte / attestazione (opzionale)
                <input name="sourceReference" maxLength={500} defaultValue={editingLexeme?.sourceReference ?? ''} placeholder="Es. Parlato urbano di Catania" />
              </label>
              <div className="admin-action-row">
                <button type="submit" className="account-primary admin-inline-primary" disabled={busy}>
                  {editingLexeme ? 'Aggiorna lemma' : 'Salva nel lessico'}
                </button>
                {editingLexeme && (
                  <button type="button" className="account-secondary" onClick={() => setEditingLexeme(null)}>
                    Nuova voce
                  </button>
                )}
              </div>
            </form>
          </div>
        </div>
      )}

      {tab === 'reports' && isAdmin && (
        <div className="learning-panel">
          <div className="learning-unit-header">
            <h2>Segnalazioni su esercizi e contenuti</h2>
            <div className="trophy-filters">
              {(['open', 'resolved', 'dismissed', 'all'] as const).map(filter => (
                <button
                  key={filter}
                  type="button"
                  className={`trophy-filter-pill ${reportFilter === filter ? 'active' : ''}`}
                  onClick={() => setReportFilter(filter)}
                >
                  {filter === 'open'
                    ? 'Aperte'
                    : filter === 'resolved'
                      ? 'Risolte'
                      : filter === 'dismissed'
                        ? 'Archiviate'
                        : 'Tutte'}
                </button>
              ))}
            </div>
          </div>

          {reports.length === 0 ? (
            <p className="admin-muted-copy">Nessuna segnalazione in questo stato.</p>
          ) : (
            <ul className="admin-card-list">
              {reports.map(report => (
                <li key={report.id} className="admin-item-card">
                  <div className="learning-unit-header">
                    <div>
                      <strong>
                        {report.targetLessonTitle ?? 'Lezione'} {report.targetExerciseId ? `· Esercizio ${report.targetExerciseId}` : ''}
                      </strong>
                      <small>
                        Da @{report.reporterUsername} · Motivo: {report.reasonCategory}
                      </small>
                    </div>
                    <span className={`admin-pill ${report.status === 'open' ? 'inReview' : 'published'}`}>{report.status}</span>
                  </div>
                  <p>{report.message}</p>
                  {report.resolutionNote && <small>Nota risoluzione: {report.resolutionNote}</small>}
                  {report.status === 'open' && (
                    <div className="admin-action-row">
                      <button
                        type="button"
                        className="account-primary admin-inline-primary"
                        disabled={busy}
                        onClick={() => void handleResolveReport(report.id, 'resolved', 'Verificato e preso in carico dalla redazione.')}
                      >
                        Segna come risolta
                      </button>
                      <button
                        type="button"
                        className="account-secondary"
                        disabled={busy}
                        onClick={() => void handleResolveReport(report.id, 'dismissed', 'Nessuna anomalia riscontrata.')}
                      >
                        Archivia
                      </button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {tab === 'users' && isAdmin && (
        <div className="learning-panel">
          <div className="learning-unit-header">
            <h2>Gestione ruoli e sospensioni account</h2>
            <input
              type="search"
              value={userSearch}
              onChange={e => setUserSearch(e.target.value)}
              placeholder="Filtra per username o nome…"
              aria-label="Filtra utenti"
              className="admin-search-input"
            />
          </div>
          <p className="admin-muted-copy">
            Bootstrap locale da terminale: <code>dotnet run --project .\src\Siculingo.Api --launch-profile local -- --grant-role &quot;username:Admin&quot;</code>
          </p>
          <ul className="admin-card-list">
            {users.map(user => {
              const isSelf = user.id === account.id
              return (
                <li key={user.id} className="admin-item-card">
                  <div className="learning-unit-header">
                    <div>
                      <strong>{user.displayName}</strong> <span>(@{user.username})</span>
                      <small>
                        {toNumber(user.totalXp)} XP · Ruoli: {user.roles.length > 0 ? user.roles.join(', ') : 'Studente'}
                      </small>
                    </div>
                    <span className={`admin-pill ${user.isSuspended ? 'suspended' : 'published'}`}>
                      {user.isSuspended ? 'Sospeso' : 'Attivo'}
                    </span>
                  </div>
                  {user.isSuspended && user.suspensionReason && <small>Motivo sospensione: {user.suspensionReason}</small>}
                  <div className="admin-action-row">
                    <button
                      type="button"
                      className="account-secondary admin-mini-btn"
                      disabled={busy}
                      onClick={() => void handleToggleRole(user, 'Editor')}
                    >
                      {user.roles.includes('Editor') ? 'Rimuovi Editor' : 'Assegna Editor'}
                    </button>
                    <button
                      type="button"
                      className="account-secondary admin-mini-btn"
                      disabled={busy || (isSelf && user.roles.includes('Admin'))}
                      onClick={() => void handleToggleRole(user, 'Admin')}
                    >
                      {user.roles.includes('Admin') ? 'Rimuovi Admin' : 'Assegna Admin'}
                    </button>
                    {!isSelf && (
                      <button
                        type="button"
                        className="account-text-button lesson-abandon-btn admin-unpublish-btn"
                        disabled={busy}
                        onClick={() => void handleToggleSuspension(user, 'Violazione regole della comunità')}
                      >
                        {user.isSuspended ? 'Riattiva account' : 'Sospendi account'}
                      </button>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        </div>
      )}

      {tab === 'metrics' && isAdmin && (
        <div className="learning-panel">
          <div className="learning-unit-header">
            <div>
              <h2>Metriche pseudonimizzate di prodotto</h2>
              <p className="admin-muted-copy">Finestra di osservazione con denominatori espliciti (nessun tracker esterno).</p>
            </div>
            <div className="trophy-filters">
              {[7, 30, 90].map(days => (
                <button
                  key={days}
                  type="button"
                  className={`trophy-filter-pill ${metricsWindowDays === days ? 'active' : ''}`}
                  onClick={() => setMetricsWindowDays(days)}
                >
                  {days} gg
                </button>
              ))}
            </div>
          </div>

          {!productMetrics ? (
            <p role="status">Calcolo metriche aggregate…</p>
          ) : (
            <>
              <div className="account-notice" role="note">
                {productMetrics.methodologyNote}
              </div>

              <div className="admin-metrics-grid" style={{ marginTop: '18px' }}>
                <div className="lesson-metric-card">
                  <span>NUOVI ACCOUNT ({toNumber(productMetrics.windowDays)} GG)</span>
                  <strong>
                    {toNumber(productMetrics.registeredUsersInWindow)} / {toNumber(productMetrics.totalRegisteredUsers)}
                  </strong>
                </div>
                <div className="lesson-metric-card">
                  <span>LEZIONI COMPLETATE / AVVIATE</span>
                  <strong>
                    {toNumber(productMetrics.lessonsCompletedInWindow)} / {toNumber(productMetrics.lessonsStartedInWindow)} (
                    {toNumber(productMetrics.lessonCompletionRatePercent)}%)
                  </strong>
                </div>
                <div className="lesson-metric-card">
                  <span>DURATA MEDIANA (TARGET 180-300 S)</span>
                  <strong>
                    {productMetrics.medianLessonDurationSeconds !== null && productMetrics.medianLessonDurationSeconds !== undefined
                      ? `${toNumber(productMetrics.medianLessonDurationSeconds)} s (${toNumber(productMetrics.shareWithinThreeToFiveMinutesPercent)}% in target)`
                      : 'N/D'}
                  </strong>
                </div>
                <div className="lesson-metric-card">
                  <span>LEZIONI / STUDENTE ATTIVO</span>
                  <strong>
                    {toNumber(productMetrics.averageCompletedLessonsPerActiveLearner)} (su{' '}
                    {toNumber(productMetrics.activeLearnersDenominator)} attivi)
                  </strong>
                </div>
                <div className="lesson-metric-card">
                  <span>RITORNO D1 (EUROPA/ROMA)</span>
                  <strong>
                    {toNumber(productMetrics.d1ReturnRatePercent)}% ({toNumber(productMetrics.d1ReturnedCount)}/
                    {toNumber(productMetrics.d1EligibleCohortCount)})
                  </strong>
                </div>
                <div className="lesson-metric-card">
                  <span>RITORNO D7 (EUROPA/ROMA)</span>
                  <strong>
                    {toNumber(productMetrics.d7ReturnRatePercent)}% ({toNumber(productMetrics.d7ReturnedCount)}/
                    {toNumber(productMetrics.d7EligibleCohortCount)})
                  </strong>
                </div>
                <div className="lesson-metric-card">
                  <span>ADOZIONE RIPASSO / PRATICA</span>
                  <strong>
                    {toNumber(productMetrics.practiceAdoptionRatePercent)}% ({toNumber(productMetrics.learnersUsingPracticeInWindow)}/
                    {toNumber(productMetrics.activeLearnersDenominator)} attivi)
                  </strong>
                </div>
                <div className="lesson-metric-card">
                  <span>ADOZIONE AMICI / PRECISIONE</span>
                  <strong>
                    {toNumber(productMetrics.friendAdoptionRatePercent)}% amici ·{' '}
                    {toNumber(productMetrics.overallExerciseAccuracyPercent)}% esatte
                  </strong>
                </div>
              </div>

              <h3 style={{ marginTop: '24px', fontSize: '16px', fontWeight: 900 }}>
                Accuratezza aggregata per esercizio e segnalazioni aperte
              </h3>
              {productMetrics.exerciseAccuracyItems.length === 0 ? (
                <p className="admin-muted-copy">Nessuna risposta registrata nella finestra selezionata.</p>
              ) : (
                <ul className="profile-ledger-list">
                  {productMetrics.exerciseAccuracyItems.map(item => (
                    <li key={`${item.lessonId}-${item.exerciseId}`}>
                      <div>
                        <strong>
                          {item.lessonTitle} · {item.exerciseId}
                        </strong>
                        <span>
                          {toNumber(item.correctAnswers)}/{toNumber(item.totalAnswers)} corrette (
                          {toNumber(item.accuracyPercent)}%)
                        </span>
                      </div>
                      <span className={`admin-pill ${toNumber(item.openReportsCount) > 0 ? 'suspended' : 'published'}`}>
                        {toNumber(item.openReportsCount) > 0
                          ? `${toNumber(item.openReportsCount)} segnalaz.`
                          : `${toNumber(item.accuracyPercent)}%`}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>
      )}

      {tab === 'audit' && isAdmin && (
        <div className="learning-panel">
          <h2>Registro tracciabilità amministrativa</h2>
          {auditItems.length === 0 ? (
            <p className="admin-muted-copy">Nessuna operazione amministrativa registrata.</p>
          ) : (
            <ul className="profile-ledger-list">
              {auditItems.map(entry => (
                <li key={String(entry.id)}>
                  <div>
                    <strong>{entry.summary}</strong>
                    <span>
                      @{entry.actorUsername} · {entry.actionType} · {new Date(entry.createdAtUtc).toLocaleString('it-IT')}
                    </span>
                  </div>
                  <span className="admin-pill draft">{entry.targetKind}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </section>
  )
}
