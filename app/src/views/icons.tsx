/**
 * Die Symbole der Bedienleiste.
 *
 * Bewusst als Strichzeichnung in einer Farbe: sie erben über `currentColor` die
 * Textfarbe und funktionieren dadurch auch auf einem aktiven Knopf, ohne dass
 * für jeden Zustand eine eigene Grafik nötig wäre.
 */

interface IconProps {
  /** Kantenlänge in Pixeln. */
  size?: number
}

/** Ein Symbol dieser Datei — für Listen, die Symbole mitführen. */
export type IconComponent = (props: IconProps) => React.JSX.Element

function Icon({ size = 22, children }: IconProps & { children: React.ReactNode }): React.JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  )
}

export function MenuIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M4 7h16M4 12h16M4 17h16" />
    </Icon>
  )
}

/** Zwei Rechner untereinander — die Geräteliste. */
export function DevicesIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <rect x="3" y="4" width="18" height="7" rx="1.5" />
      <rect x="3" y="14" width="18" height="6" rx="1.5" />
      <path d="M6.5 7.5h.01M6.5 17h.01" />
    </Icon>
  )
}

/**
 * Ein Bildschirm auf einem Fuß — ein Rechner in der Geräteliste.
 *
 * Das Symbol sagt, was ein Gerät *ist*, und nicht, was es kann. Es steht auch
 * dann da, wenn das Gerät gerade aus ist: die Angabe kommt aus der Kopplung
 * und nicht aus einer Anfrage.
 */
export function ComputerIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <rect x="3" y="4" width="18" height="12" rx="1.5" />
      <path d="M9 20h6M12 16v4" />
    </Icon>
  )
}

/** Ein Handy — hochkant, mit dem Strich für den Lautsprecher. */
export function PhoneIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <rect x="7" y="2.5" width="10" height="19" rx="2" />
      <path d="M10.5 5.5h3" />
    </Icon>
  )
}

/**
 * Ein Stift — „umbenennen", direkt neben dem Namen.
 *
 * Zwei Striche und ein Winkel: er steht neben einem Wort und darf es nicht
 * überstimmen. Alles, was mehr Kanten hätte — Radiergummi, Schraffur, Spitze —
 * wäre bei 14 Pixeln ohnehin nur Rauschen.
 */
export function PencilIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M4 20h4L19 9l-4-4L4 16z" />
    </Icon>
  )
}

/** Zahnrad — die Einstellungen der App. */
export function SettingsIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="3" />
      <path
        d="M12 2.5v2.2M12 19.3v2.2M21.5 12h-2.2M4.7 12H2.5M18.7 5.3l-1.6 1.6M6.9 17.1l-1.6
           1.6M18.7 18.7l-1.6-1.6M6.9 6.9L5.3 5.3"
      />
    </Icon>
  )
}

export function KeyboardIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <rect x="2.5" y="6" width="19" height="12" rx="2" />
      <path d="M6.5 9.5h.01M10 9.5h.01M13.5 9.5h.01M17 9.5h.01M8 14.5h8" />
    </Icon>
  )
}

export function MouseIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <rect x="7" y="2.5" width="10" height="19" rx="5" />
      <path d="M12 6.5v3" />
    </Icon>
  )
}

export function MediaIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M9.5 6.5v11l8-5.5z" />
    </Icon>
  )
}

export function PowerIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M12 3.5v7.5" />
      <path d="M17.5 6.5a7.5 7.5 0 1 1-11 0" />
    </Icon>
  )
}

export function ScreenIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <rect x="2.5" y="4" width="19" height="13" rx="2" />
      <path d="M9 20.5h6" />
    </Icon>
  )
}

export function TextIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M5 6.5h14M5 12h14M5 17.5h8" />
    </Icon>
  )
}

export function ShortcutIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M13 2.5 4.5 13.5H11l-1 8 8.5-11H12z" />
    </Icon>
  )
}

export function PrevIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M18.5 5.5v13L9 12z" />
      <path d="M5.5 5.5v13" />
    </Icon>
  )
}

export function NextIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M5.5 5.5v13L15 12z" />
      <path d="M18.5 5.5v13" />
    </Icon>
  )
}

export function PlayPauseIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <path d="M3.5 5.5v13L12 12z" />
      <path d="M16 6v12M20.5 6v12" />
    </Icon>
  )
}

/** Lautsprecher ohne Wellen — die Anzahl der Wellen macht die Lautstärke aus. */
function Speaker(): React.JSX.Element {
  return <path d="M4 9.5h3l4-3.5v12l-4-3.5H4z" />
}

export function VolumeDownIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <Speaker />
      <path d="M14.5 9.75a3 3 0 0 1 0 4.5" />
    </Icon>
  )
}

export function VolumeUpIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <Speaker />
      <path d="M14.5 9.75a3 3 0 0 1 0 4.5" />
      <path d="M17.5 7a7 7 0 0 1 0 10" />
    </Icon>
  )
}

export function VolumeMuteIcon(props: IconProps): React.JSX.Element {
  return (
    <Icon {...props}>
      <Speaker />
      <path d="M15 9.5 20 14.5M20 9.5 15 14.5" />
    </Icon>
  )
}
