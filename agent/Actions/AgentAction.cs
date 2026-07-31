using System.Text.Json.Serialization;

namespace RemoteDesktopAgent.Actions;

/// <summary>
/// Was eine Aktion tut. Der Client kennt diese Namen nur, um ein Symbol zu
/// wählen — was tatsächlich passiert, entscheidet allein diese Datei.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionType>))]
public enum ActionType
{
    /// <summary>Ein Programm starten, Argumente als Array.</summary>
    Process,

    /// <summary>Ein hinterlegtes PowerShell-Skript starten.</summary>
    Script,

    /// <summary>Eine Tastenkombination senden.</summary>
    Keys,

    /// <summary>Eine Adresse im Standardbrowser öffnen.</summary>
    Url,

    /// <summary>Mehrere Aktionen nacheinander, mit Pausen dazwischen.</summary>
    Sequence
}

/// <summary>
/// Ein Schritt einer Sequenz: entweder eine andere Aktion oder eine Pause.
/// </summary>
public sealed record ActionStep(string? Action, int? DelayMs);

/// <summary>
/// Eine Aktion, wie sie in <c>actions.json</c> steht.
///
/// <para>
/// <b>Die eine Regel:</b> deklariert wird hier, aufgerufen wird per
/// <see cref="Id"/>. Der Client schickt nie eine Kommandozeile — er schickt eine
/// Kennung, und was sie bedeutet, steht ausschließlich auf diesem Rechner.
/// Alles andere wäre absichtlich gebaute Remote-Code-Execution.
/// </para>
///
/// <para>
/// Deshalb ist <see cref="Args"/> ein Array und keine Zeichenkette: es gibt
/// keine Zeile, die jemand zusammensetzen könnte, und damit auch nichts, in das
/// sich etwas einschleusen ließe. Dieselbe Linie hält der Agent schon bei
/// ffmpeg (siehe <c>docs/SICHERHEIT.md</c>).
/// </para>
/// </summary>
/// <param name="Confirm">
/// Verlangt eine Rückfrage im Client, bevor ausgelöst wird. Der Agent verlässt
/// sich nicht darauf — er führt trotzdem aus, wenn der Aufruf kommt. Der Merker
/// schützt vor dem verrutschten Daumen, nicht vor einem bösen Client; davor
/// schützt die Kopplung.
/// </param>
public sealed record AgentAction(
    string? Id,
    string? Label,
    string? Icon,
    ActionType Type,
    string? File,
    IReadOnlyList<string>? Args,
    string? WorkingDirectory,
    IReadOnlyList<string>? Chord,
    string? Url,
    IReadOnlyList<ActionStep>? Steps,
    bool Confirm);

/// <summary>
/// Was <c>GET /api/actions</c> herausgibt.
///
/// Ausdrücklich <b>ohne</b> Pfade, Argumente und Arbeitsverzeichnis: ein Client
/// braucht sie nicht, um einen Knopf zu bauen, und wer die Liste abfragen darf,
/// muss nicht auch erfahren, welche Software auf dem Rechner liegt und wo.
/// </summary>
public sealed record ActionSummary(string Id, string Label, string? Icon, string Type, bool Confirm);
