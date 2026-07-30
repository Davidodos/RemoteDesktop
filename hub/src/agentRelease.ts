import { createHash } from 'node:crypto'
import { createReadStream } from 'node:fs'
import { stat } from 'node:fs/promises'
import { resolve } from 'node:path'
import { Router } from 'express'

/** Wo die veröffentlichte Agent-Datei liegt. Im Container gemountet. */
const RELEASE_DIR = process.env.AGENT_RELEASE_PATH ?? '/release'

const EXECUTABLE = 'RemoteDesktopAgent.exe'

/** Merker für die Prüfsumme, damit nicht bei jeder Anfrage 97 MB gelesen werden. */
interface ReleaseInfo {
  size: number
  modifiedAt: number
  sha256: string
}

let cached: ReleaseInfo | undefined

/**
 * Stellt die aktuelle Agent-Datei bereit, damit sich der Agent selbst
 * aktualisieren kann.
 *
 * Die Version steckt bewusst nicht in einer separaten Datei, sondern ergibt
 * sich aus der Prüfsumme: so kann niemand vergessen, sie hochzuzählen, und der
 * Agent lädt nur dann, wenn sich die Datei wirklich geändert hat.
 */
export function createReleaseRouter(): Router {
  const router = Router()

  router.get('/agent/manifest', async (_request, response) => {
    const info = await describeRelease()

    if (info === undefined) {
      response.status(404).json({ error: 'Keine Agent-Datei hinterlegt.' })
      return
    }

    response.json({ file: EXECUTABLE, size: info.size, sha256: info.sha256 })
  })

  router.get('/agent/download', async (_request, response) => {
    const info = await describeRelease()

    if (info === undefined) {
      response.status(404).json({ error: 'Keine Agent-Datei hinterlegt.' })
      return
    }

    response.type('application/octet-stream')
    createReadStream(path()).pipe(response)
  })

  return router
}

function path(): string {
  return resolve(RELEASE_DIR, EXECUTABLE)
}

async function describeRelease(): Promise<ReleaseInfo | undefined> {
  let stats

  try {
    stats = await stat(path())
  } catch {
    return undefined
  }

  if (
    cached !== undefined &&
    cached.size === stats.size &&
    cached.modifiedAt === stats.mtimeMs
  ) {
    return cached
  }

  cached = {
    size: stats.size,
    modifiedAt: stats.mtimeMs,
    sha256: await hashFile(path()),
  }

  return cached
}

function hashFile(file: string): Promise<string> {
  return new Promise((resolveHash, rejectHash) => {
    const hash = createHash('sha256')
    const stream = createReadStream(file)

    stream.on('data', (chunk) => hash.update(chunk))
    stream.on('end', () => resolveHash(hash.digest('hex')))
    stream.on('error', rejectHash)
  })
}
