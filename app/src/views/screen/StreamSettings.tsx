import type { QualityMode } from '../../lib/types.ts'

/**
 * H.264 über WebRTC ist der sparsamere Weg, läuft aber nur, wenn ffmpeg auf
 * dem Rechner liegt und die Grafikkarte einen Encoder mitbringt. Der
 * JPEG-Stream läuft überall.
 */
export type Transport = 'webrtc' | 'jpeg'

const TRANSPORTS: { mode: Transport; label: string }[] = [
  { mode: 'webrtc', label: 'H.264' },
  { mode: 'jpeg', label: 'JPEG' },
]

const QUALITIES: { mode: QualityMode; label: string }[] = [
  { mode: 'auto', label: 'Auto' },
  { mode: 'high', label: 'Scharf' },
  { mode: 'medium', label: 'Mittel' },
  { mode: 'low', label: 'Sparsam' },
]

interface Props {
  transport: Transport
  quality: QualityMode
  /** Ob der gerade gezeigte Monitor schon der Standard dieses Geräts ist. */
  isDefaultMonitor: boolean
  onTransport: (mode: Transport) => void
  onQuality: (mode: QualityMode) => void
  onDefaultMonitor: () => void
}

/**
 * Übertragungsweg und Bildqualität.
 *
 * Steckt hinter dem Zahnrad, weil beides einmal eingestellt und dann monatelang
 * nicht mehr angefasst wird — der Platz gehört dem Bild.
 */
export function StreamSettings({
  transport,
  quality,
  isDefaultMonitor,
  onTransport,
  onQuality,
  onDefaultMonitor,
}: Props): React.JSX.Element {
  return (
    <div className="stream-settings">
      {/* Merkt sich den gezeigten Monitor für dieses Gerät — beim nächsten Mal
          steht das Bild sofort richtig. */}
      <button
        type="button"
        className={isDefaultMonitor ? 'quality-button active' : 'quality-button'}
        disabled={isDefaultMonitor}
        onClick={onDefaultMonitor}
      >
        {isDefaultMonitor ? 'Standard ✓' : 'Als Standard'}
      </button>

      {/* Zwei getrennte Knöpfe statt eines Umschalters: bei einem einzelnen ist
          nie klar, ob die Beschriftung den aktuellen Zustand zeigt oder das,
          was ein Druck bewirkt. */}
      {TRANSPORTS.map(({ mode, label }) => (
        <button
          key={mode}
          type="button"
          className={transport === mode ? 'quality-button active' : 'quality-button'}
          onClick={() => onTransport(mode)}
        >
          {label}
        </button>
      ))}

      {/* Die Qualitätsstufen gelten nur für den JPEG-Weg — bei H.264 regelt das
          der Encoder auf dem Rechner. */}
      {transport === 'jpeg' &&
        QUALITIES.map(({ mode, label }) => (
          <button
            key={mode}
            type="button"
            className={quality === mode ? 'quality-button active' : 'quality-button'}
            onClick={() => onQuality(mode)}
          >
            {label}
          </button>
        ))}
    </div>
  )
}
