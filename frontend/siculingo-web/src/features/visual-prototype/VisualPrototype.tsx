// Throwaway: four guide screens and three course-path layouts at /prototype?variant=A|B|C.
import { useEffect, useRef, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import './prototype.css'

type Screen = 'ingresso' | 'percorso' | 'esercizio' | 'riepilogo'
type Variant = 'A' | 'B' | 'C'
type Answer = { correct: boolean; selected: number }
type GlyphName = 'arrow' | 'check' | 'close' | 'lock' | 'star' | 'clock' | 'chevron' | 'sparkle'

const screens: { id: Screen; label: string }[] = [
  { id: 'ingresso', label: 'Ingresso' },
  { id: 'percorso', label: 'Percorso' },
  { id: 'esercizio', label: 'Esercizio' },
  { id: 'riepilogo', label: 'Riepilogo' },
]
const variants: { id: Variant; label: string }[] = [
  { id: 'A', label: 'Il sentiero' },
  { id: 'B', label: 'Il quaderno' },
  { id: 'C', label: 'Un passo alla volta' },
]
const files = {
  logo: 'brand/siculingo-logo.png',
  front: 'mascot/ficodi-front.png',
  study: 'mascot/ficodi-study.png',
  happy: 'mascot/ficodi-happy.png',
  proud: 'mascot/ficodi-proud.png',
  victory: 'mascot/ficodi-victory.png',
  home: 'icons/home.png',
  book: 'icons/book.png',
  practice: 'icons/practice.png',
  trophy: 'icons/trophy.png',
  flame: 'icons/flame.png',
  heart: 'icons/heart.png',
  friends: 'icons/friends.png',
  goal: 'icons/goal.png',
  shield: 'badges/shield.png',
  star: 'badges/star.png',
  xp: 'badges/xp.png',
} as const
const lessons = [
  { name: 'Le prime parole', description: 'Un saluto è già un inizio.', status: 'done' },
  { name: 'Piacere di conoscerti', description: 'Presentati, a modo nostro.', status: 'current' },
  { name: 'Come stai?', description: 'Le piccole conversazioni.', status: 'locked' },
  { name: 'Ci vediamo presto', description: 'Ogni saluto è un arrivederci.', status: 'locked' },
  { name: 'Facciamo due chiacchiere', description: 'Metti insieme le tue parole.', status: 'locked' },
] as const
const questions = [
  { prompt: 'Ciao', title: 'Scegli il saluto giusto', options: ['Salutamu', 'Grazzi', 'Bonasira', 'A dumani'], correct: 0, explanation: 'In questa bozza usiamo “Salutamu” per un saluto informale.' },
  { prompt: 'Buonasera', title: 'Un saluto, verso sera', options: ['A dumani', 'Bonasira', 'Grazzi', 'Salutamu'], correct: 1, explanation: 'In questa bozza “Bonasira” corrisponde a “Buonasera”.' },
  { prompt: 'A domani', title: 'Ci rivediamo presto', options: ['Bonasira', 'Grazzi', 'A dumani', 'Salutamu'], correct: 2, explanation: 'In questa bozza usiamo “A dumani” per “A domani”.' },
]

function locationState(): { screen: Screen; variant: Variant } {
  const params = new URLSearchParams(window.location.search)
  const screen = screens.find((item) => item.id === params.get('screen'))?.id ?? 'percorso'
  const variant = variants.find((item) => item.id === params.get('variant'))?.id ?? 'A'
  return { screen, variant }
}

function Asset({ name, className = '', alt = '' }: { name: keyof typeof files; className?: string; alt?: string }) {
  return <img className={`asset ${className}`} src={`/assets/${files[name]}`} alt={alt} draggable="false" />
}

function Glyph({ name, className = '' }: { name: GlyphName; className?: string }) {
  const paths: Record<GlyphName, ReactNode> = {
    arrow: <path d="M4 12h15m-6-6 6 6-6 6" />,
    check: <path d="m5 12 4 4L19 6" />,
    close: <path d="m6 6 12 12M6 18 18 6" />,
    lock: <><rect x="5" y="10" width="14" height="11" rx="3" /><path d="M8 10V7a4 4 0 0 1 8 0v3m-4 5v2" /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9Z" />,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    chevron: <path d="m9 6 6 6-6 6" />,
    sparkle: <path d="m12 2 2.5 7.5L22 12l-7.5 2.5L12 22l-2.5-7.5L2 12l7.5-2.5Z" />,
  }
  return <svg className={`glyph ${className}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

function Progress({ value, label, className = '' }: { value: number; label: string; className?: string }) {
  return <div className={`progress ${className}`} role="progressbar" aria-label={label} aria-valuenow={value} aria-valuemin={0} aria-valuemax={100}><span style={{ width: `${value}%` }} /></div>
}

function PrototypeSwitcher({ variant, onChange }: { variant: Variant; onChange: (variant: Variant) => void }) {
  const index = variants.findIndex((item) => item.id === variant)
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      const target = event.target
      if (target instanceof HTMLElement && target.closest('input, textarea, select, button, [contenteditable="true"], [role="radio"]')) return
      if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
      event.preventDefault()
      onChange(variants[(index + (event.key === 'ArrowRight' ? 1 : 2)) % 3].id)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [index, onChange])
  if (!import.meta.env.DEV) return null
  return <div className="variant-switcher" aria-label="Confronta tre layout del percorso">
    <button aria-label="Variante precedente" onClick={() => onChange(variants[(index + 2) % 3].id)}><Glyph name="arrow" className="reverse" /></button>
    <div><small>LAYOUT DEL PERCORSO</small><strong>{variant} <span>·</span> {variants[index].label}</strong></div>
    <button aria-label="Variante successiva" onClick={() => onChange(variants[(index + 1) % 3].id)}><Glyph name="arrow" /></button>
  </div>
}

function Sidebar({ go }: { go: (screen: Screen) => void }) {
  return <aside className="sidebar">
    <button className="brand-button" aria-label="Siculingo, schermata di ingresso" onClick={() => go('ingresso')}><Asset name="logo" alt="Siculingo" /></button>
    <span className="brand-caption">Le tue radici. Le tue parole.</span>
    <nav aria-label="Navigazione principale">
      {([
        ['home', 'Impara', true], ['trophy', 'Classifica', false], ['friends', 'Amici', false], ['shield', 'Trofei', false],
      ] as const).map(([icon, label, active]) => <button key={label} className={`nav-item ${active ? 'active' : ''}`} onClick={() => go('percorso')} disabled={!active} title={active ? undefined : 'Previsto nelle prossime tappe'} aria-current={active ? 'page' : undefined}>
        <Asset name={icon} /><span>{label}</span>{!active && <Glyph name="lock" />}
      </button>)}
    </nav>
    <div className="sidebar-bottom">
      <div className="course-language"><span className="sicily-mark" aria-hidden="true">S</span><div><strong>Italiano → Catanese</strong><small>Il tuo primo viaggio</small></div></div>
      <div className="profile-chip"><span className="avatar">E</span><div><strong>Esploratore</strong><small>Profilo dimostrativo</small></div><span className="demo-tag">DEMO</span></div>
    </div>
  </aside>
}

function Stats({ hearts }: { hearts: number }) {
  return <div className="stats-row" aria-label="Statistiche dimostrative">
    <span className="language-chip"><span aria-hidden="true">IT</span> Catanese</span>
    <div className="stats-values">
      <span className="stat streak"><Asset name="flame" /><strong>7</strong><span className="sr-only">giorni di serie, esempio</span></span>
      <span className="stat hearts"><Asset name="heart" /><strong>{hearts}</strong><span className="sr-only">cuori nella demo</span></span>
      <span className="stat xp"><Glyph name="sparkle" /><strong>120</strong><span className="sr-only">XP, esempio</span></span>
    </div>
  </div>
}

function DailyCards() {
  return <aside className="daily-rail" aria-label="Obiettivi dimostrativi">
    <section className="daily-card greeting-card"><div className="tiny-label">UN PASSO AL GIORNO</div><Asset name="front" className="rail-mascot" /><h3>La costanza parla siciliano.</h3><p>Anche cinque minuti possono farti sentire un po’ più a casa.</p><span className="small-note">Ficodì fa il tifo per te.</span></section>
    <section className="daily-card"><div className="card-title"><h3>Il tuo obiettivo</h3><Asset name="goal" /></div><div className="goal-numbers"><strong>10 <span>/ 20 XP</span></strong><span>50%</span></div><Progress value={50} label="Esempio obiettivo giornaliero: 50 percento" /><p>Ti manca una piccola lezione.</p><small>Dati di esempio, non salvati</small></section>
    <section className="daily-card streak-card"><Asset name="flame" /><div><h3>7 giorni, bella costanza!</h3><p>Una parola dopo l’altra.</p></div><div className="week-days">{['L', 'M', 'M', 'G', 'V', 'S', 'D'].map((day, i) => <span key={i} className={i < 5 ? 'complete' : ''}>{i < 5 ? <Glyph name="check" /> : day}<span className="sr-only">{day}</span></span>)}</div></section>
  </aside>
}

function UnitHeader() {
  return <header className="unit-banner"><div><span className="tiny-label">SEZIONE 1 · UNITÀ PILOTA</span><h2>Salutamu!</h2><p>Le prime parole per sentirsi a casa.</p></div><div className="unit-counter"><Asset name="book" /><span>1 / 5 lezioni</span></div></header>
}

function VariantA({ start }: { start: () => void }) {
  return <div className="path-layout variant-a">
    <UnitHeader />
    <div className="winding-path">
      <svg className="path-line" viewBox="0 0 480 568" preserveAspectRatio="none" aria-hidden="true"><path d="M240 38C240 110 340 96 340 165S140 226 140 298s100 51 100 118 105 53 105 107" /></svg>
      {lessons.map((lesson, i) => <div key={lesson.name} className={`path-step step-${i} ${lesson.status}`} style={{ '--step': i } as CSSProperties}>
        {lesson.status === 'current' && <span className="start-bubble">SI RIPARTE DA QUI <span>↓</span></span>}
        <button className="lesson-node" onClick={start} disabled={lesson.status === 'locked'} aria-label={`${lesson.name}${lesson.status === 'locked' ? ', bloccata' : ', apri demo'}`}>
          <Glyph name={lesson.status === 'done' ? 'check' : lesson.status === 'locked' ? 'lock' : 'star'} />
        </button>
        <div className="node-caption"><strong>{lesson.name}</strong>{lesson.status === 'current' && <span>3 min · Lezione 2</span>}</div>
      </div>)}
      <div className="path-companion"><Asset name="study" /><span>Una parola alla volta!</span></div>
      <div className="path-flower flower-one" aria-hidden="true">✳</div><div className="path-flower flower-two" aria-hidden="true">✳</div>
    </div>
    <button className="button primary continue-path" onClick={start}>Continua a imparare <Glyph name="arrow" /></button>
    <p className="content-note">Percorso e progressi dimostrativi · testi da revisionare</p>
  </div>
}

function VariantB({ start }: { start: () => void }) {
  return <div className="notebook-layout variant-b"><header className="notebook-heading"><span className="eyebrow">IL TUO QUADERNO · CAPITOLO 01</span><h2>Ogni incontro<br />inizia con un saluto.</h2><p>Una piccola raccolta di parole da portare con te.</p><Asset name="study" /></header><div className="notebook-progress"><strong>Il primo capitolo</strong><span>1 lezione su 5</span><Progress value={20} label="Una lezione di esempio su cinque" /></div><ol className="notebook-lessons">{lessons.map((lesson, i) => <li key={lesson.name} className={lesson.status}><span className="lesson-number">{lesson.status === 'done' ? <Glyph name="check" /> : `0${i + 1}`}</span><div><h3>{lesson.name}</h3><p>{lesson.description}</p></div><button aria-label={`Apri ${lesson.name}`} onClick={start} disabled={lesson.status === 'locked'}><Glyph name={lesson.status === 'locked' ? 'lock' : 'arrow'} /></button></li>)}</ol><p className="content-note">Stesso contenuto, struttura editoriale · dati dimostrativi</p></div>
}

function VariantC({ start }: { start: () => void }) {
  return <div className="focus-layout variant-c"><span className="eyebrow">OGGI BASTA UN PICCOLO PASSO</span><h2>Fai spazio a<br />una nuova parola.</h2><div className="focus-orbit"><span className="orbit-dot dot-one" /><span className="orbit-dot dot-two" /><Asset name="front" /><span className="orbit-note"><Glyph name="clock" /> Solo 3 minuti</span></div><section className="focus-lesson"><span className="tiny-label">SALUTAMU! · LEZIONE 2 DI 5</span><h3>Piacere di conoscerti</h3><p>Presentati e inizia la tua prima conversazione.</p><button className="button primary" onClick={start}>Facciamo due parole <Glyph name="arrow" /></button></section><div className="focus-milestones" aria-label="Esempio avanzamento delle cinque lezioni">{lessons.map((lesson, i) => <span key={lesson.name} className={lesson.status} aria-label={`${lesson.name}: ${lesson.status}`}><Glyph name={i === 0 ? 'check' : i === 1 ? 'star' : 'lock'} /></span>)}</div><p className="content-note">Un solo invito all’azione · progressi di esempio</p></div>
}

function Welcome({ go }: { go: (screen: Screen) => void }) {
  return <main id="main-content" className="welcome-screen">
    <header><Asset name="logo" alt="Siculingo" /><span className="welcome-language">ITALIANO → CATANESE</span></header>
    <div className="welcome-body">
      <div className="welcome-copy">
        <span className="eyebrow"><span className="little-sun" /> PAROLE VICINE. RADICI PROFONDE.</span>
        <h1>Un po’ di Sicilia.<br /><span>Ogni giorno.</span></h1>
        <p>Riscopri il piacere di parlare catanese.<br />Piccole lezioni, grandi sorrisi. Insieme a Ficodì.</p>
        <button className="button primary" onClick={() => go('percorso')}>Inizia il tuo viaggio <Glyph name="arrow" /></button>
        <span className="welcome-disclaimer">Esplora la demo · nessun account necessario</span>
        <div className="welcome-benefits"><span><Glyph name="clock" /> 3–5 minuti al giorno</span><span><Glyph name="check" /> Un passo alla volta</span></div>
      </div>
      <div className="welcome-art">
        <div className="art-ring ring-one" /><div className="art-ring ring-two" />
        <span className="art-sun" aria-hidden="true">✳</span>
        <div className="mascot-greeting">
          <Asset name="front" alt="Ficodì, la mascotte di Siculingo" />
          <div className="speech-bubble">Salutamu!<small>Si parte insieme?</small></div>
        </div>
        <div className="art-caption">
          <span className="tiny-label">LA TUA GUIDA, FICODÌ</span>
          <h2>Le parole di casa<br />viaggiano con te.</h2>
          <span className="island-label">37°30′ N · 15°05′ E <span>CATANIA, SICILIA</span></span>
        </div>
        <div className="tile-border" aria-hidden="true" />
      </div>
    </div>
    <footer><span>Fatto di parole, persone e un po’ di sole.</span><span>Prototipo visivo · niente registrazione reale</span></footer>
  </main>
}

export default function VisualPrototype() {
  const [route, setRoute] = useState(locationState)
  const [answers, setAnswers] = useState<Answer[]>([])
  const [selected, setSelected] = useState<number | null>(null)
  const [feedback, setFeedback] = useState<boolean | null>(null)
  const [exitOpen, setExitOpen] = useState(false)
  const heading = useRef<HTMLHeadingElement>(null)
  const questionIndex = Math.min(answers.length, questions.length - 1)
  const question = questions[questionIndex]
  const hearts = 5 - answers.filter((answer) => !answer.correct).length
  const completed = answers.length === questions.length
  const correct = completed ? answers.filter((answer) => answer.correct).length : 3
  const xp = 10 + correct * 2

  function navigate(screen: Screen, variant = route.variant) {
    const url = new URL(window.location.href)
    url.pathname = '/prototype'
    url.searchParams.set('screen', screen)
    url.searchParams.set('variant', variant)
    window.history.pushState(null, '', url)
    setRoute({ screen, variant })
    window.scrollTo({ top: 0, behavior: 'instant' })
  }

  function startLesson() {
    setAnswers([])
    setSelected(null)
    setFeedback(null)
    navigate('esercizio')
  }

  function submit() {
    if (selected === null) return
    if (feedback === null) {
      setFeedback(selected === question.correct)
      return
    }
    const next = [...answers, { selected, correct: feedback }]
    setAnswers(next)
    setSelected(null)
    setFeedback(null)
    if (next.length === questions.length) navigate('riepilogo')
  }

  useEffect(() => {
    function onPopState() {
      const next = locationState()
      if (next.screen === 'esercizio') {
        setAnswers([])
        setSelected(null)
        setFeedback(null)
      }
      setRoute(next)
      setExitOpen(false)
    }
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])
  useEffect(() => { heading.current?.focus() }, [route.screen])
  useEffect(() => {
    if (!exitOpen) return
    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape') setExitOpen(false)
      if (event.key === 'Tab') {
        const buttons = document.querySelectorAll<HTMLButtonElement>('.exit-dialog button')
        const first = buttons[0]
        const last = buttons[buttons.length - 1]
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last?.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first?.focus()
        }
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [exitOpen])

  return <>
    <a className="skip-link" href="#main-content">Vai al contenuto</a>
    <div className="studio-bar">
      <span className="studio-label"><span /> STUDIO SICULINGO <small>PROTOTIPO · DATI DEMO</small></span>
      <nav aria-label="Quattro schermate guida">
        {screens.map((screen, i) => <button key={screen.id} aria-current={route.screen === screen.id ? 'page' : undefined} onClick={() => screen.id === 'esercizio' ? startLesson() : navigate(screen.id)}><small>0{i + 1}</small>{screen.label}</button>)}
      </nav>
    </div>
    {route.screen === 'ingresso' ? <Welcome go={navigate} /> : route.screen === 'esercizio' ? <main id="main-content" className="exercise-screen">
      <header className="exercise-top"><button className="icon-button" aria-label="Esci dalla lezione demo" onClick={() => setExitOpen(true)}><Glyph name="close" /></button><Progress value={((answers.length + (feedback === null ? 0 : 1)) / questions.length) * 100} label={`Esercizio ${answers.length + 1} di ${questions.length}`} /><span className="stat hearts"><Asset name="heart" /><strong>{hearts - (feedback === false ? 1 : 0)}</strong></span></header>
      <section className="exercise-body"><div className="exercise-kicker"><span className="eyebrow">LE PRIME PAROLE</span><span>{questionIndex + 1} di 3 · demo</span></div><h1 ref={heading} tabIndex={-1}>{question.title}</h1><p className="exercise-description">Come lo diresti in catanese?</p><div className="question-prompt"><Asset name="study" /><div className="prompt-bubble"><span>ITALIANO</span><strong>{question.prompt}</strong></div></div><div className="answer-options" role="group" aria-label="Scegli una risposta">{question.options.map((option, i) => <button key={option} disabled={feedback !== null} aria-pressed={selected === i} className={`answer ${selected === i ? 'selected' : ''} ${feedback !== null && i === question.correct ? 'correct' : ''} ${feedback === false && selected === i ? 'wrong' : ''}`} onClick={() => setSelected(i)}><span className="option-number">{i + 1}</span><strong>{option}</strong>{feedback !== null && i === question.correct && <Glyph name="check" />}</button>)}</div><p className="content-note">Contenuti linguistici dimostrativi, in attesa di revisione.</p></section>
      <footer className={`exercise-footer ${feedback === true ? 'positive' : feedback === false ? 'negative' : ''}`}><div aria-live="polite">{feedback !== null ? <><div className="feedback-title"><Glyph name={feedback ? 'check' : 'close'} /><strong>{feedback ? 'Proprio così. Bravissimo!' : 'Nessun problema, si impara così.'}</strong></div><p>{question.explanation}</p></> : <span className="footer-hint">Scegli con calma. Ogni parola è un piccolo passo.</span>}</div><button className="button primary" disabled={selected === null} onClick={submit}>{feedback === null ? 'Controlla' : 'Continua'}<Glyph name="arrow" /></button></footer>
    </main> : route.screen === 'riepilogo' ? <main id="main-content" className="summary-screen"><button className="text-button summary-back" onClick={() => navigate('percorso')}><Glyph name="arrow" className="reverse" /> Torna al percorso</button><div className="celebration-stage"><span className="celebration-spark spark-one">✦</span><span className="celebration-spark spark-two">✦</span><Asset name="victory" alt="Ficodì festeggia con una coppa" /></div><span className="eyebrow">UN ALTRO PICCOLO TRAGUARDO</span><h1 ref={heading} tabIndex={-1}>Bravo, parola dopo parola!</h1><p>Oggi la Sicilia è un po’ più vicina.</p><div className="summary-stats"><div><Asset name="xp" /><span>XP DEMO</span><strong>+{xp}</strong></div><div><Asset name="goal" /><span>RISPOSTE CORRETTE</span><strong>{correct}<small> / 3</small></strong></div><div><Asset name="flame" /><span>SERIE DI ESEMPIO</span><strong>7 <small>giorni</small></strong></div></div><div className="summary-encouragement"><Asset name="happy" /><div><strong>{correct === 3 ? 'Tre risposte, un bel sorriso.' : 'Ogni tentativo ti porta avanti.'}</strong><p>{completed ? 'Hai completato il percorso dimostrativo.' : 'Questo è un esempio di riepilogo, non una lezione svolta.'}</p></div></div><button className="button primary" onClick={() => navigate('percorso')}>Continua il viaggio <Glyph name="arrow" /></button><button className="text-button" onClick={startLesson}>Riprova la lezione demo</button><p className="content-note">Nessun XP, trofeo o progresso reale viene assegnato.</p></main> : <div className="app-shell"><Sidebar go={navigate} /><main id="main-content" className="learning-main"><Stats hearts={hearts} /><div className="learning-heading"><div><span className="eyebrow">IL TUO VIAGGIO, A MODO TUO</span><h1 ref={heading} tabIndex={-1}>Bentornato, esploratore<span>!</span></h1><p>Le parole di casa ti aspettano.</p></div><span className="level-chip"><Glyph name="star" /> Livello 2 <small>demo</small></span></div><div className={`learning-columns layout-${route.variant}`}><div className="course-column">{route.variant === 'A' ? <VariantA start={startLesson} /> : route.variant === 'B' ? <VariantB start={startLesson} /> : <VariantC start={startLesson} />}</div><DailyCards /></div></main></div>}
    {import.meta.env.DEV && route.screen === 'percorso' && <PrototypeSwitcher variant={route.variant} onChange={(variant) => {
      const url = new URL(window.location.href)
      url.searchParams.set('variant', variant)
      window.history.replaceState(null, '', url)
      setRoute({ ...route, variant })
    }} />}
    <details className="prototype-state"><summary>Stato demo · {route.screen} · {route.variant}</summary><pre>{JSON.stringify({ ...route, answers, selected, feedback, hearts, persistentData: false, languageContent: 'awaiting-review' }, null, 2)}</pre><button className="text-button" onClick={() => { setAnswers([]); setSelected(null); setFeedback(null); navigate('percorso') }}>Azzera la demo</button></details>
    {exitOpen && <div className="dialog-backdrop"><section className="exit-dialog" role="dialog" aria-modal="true" aria-labelledby="exit-title"><Asset name="proud" /><h2 id="exit-title">Una piccola pausa?</h2><p>Questa è una demo: uscendo, le risposte della sessione saranno azzerate.</p><button className="button primary" autoFocus onClick={() => setExitOpen(false)}>Resto e continuo</button><button className="text-button" onClick={() => { setExitOpen(false); setAnswers([]); setSelected(null); setFeedback(null); navigate('percorso') }}>Esci dalla demo</button></section></div>}
  </>
}
