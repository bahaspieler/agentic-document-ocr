import { useEffect, useState, type ReactNode } from 'react'

type TokenProvider = () => Promise<string | undefined>

export function AuthenticatedArtifactImage({ url, alt, className, getToken }: {
  url: string
  alt: string
  className?: string
  getToken?: TokenProvider
}) {
  const [source, setSource] = useState(getToken ? undefined : url)

  useEffect(() => {
    if (!getToken) {
      setSource(url)
      return
    }

    let active = true
    let objectUrl: string | undefined
    void fetchArtifact(url, getToken).then((blob) => {
      if (!active) return
      objectUrl = URL.createObjectURL(blob)
      setSource(objectUrl)
    }).catch(() => {
      if (active) setSource(undefined)
    })

    return () => {
      active = false
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [getToken, url])

  return source
    ? <img className={className} src={source} alt={alt} />
    : <span className={className} role="status" aria-label={`${alt} loading`} />
}

export function AuthenticatedArtifactAction({
  url,
  fileName,
  getToken,
  children,
  ariaLabel,
  download = false,
}: {
  url: string
  fileName: string
  getToken?: TokenProvider
  children: ReactNode
  ariaLabel?: string
  download?: boolean
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  if (!getToken) {
    return <a
      aria-label={ariaLabel}
      href={url}
      target={download ? undefined : '_blank'}
      rel={download ? undefined : 'noreferrer'}
      download={download ? fileName : undefined}
    >{children}</a>
  }

  const open = async () => {
    setBusy(true)
    setError(undefined)
    const target = download ? null : window.open('', '_blank')
    if (target) target.opener = null
    try {
      const blob = await fetchArtifact(url, getToken)
      const objectUrl = URL.createObjectURL(blob)
      if (download) {
        const link = document.createElement('a')
        link.href = objectUrl
        link.download = fileName
        link.click()
      } else {
        if (target) target.location.href = objectUrl
        else throw new Error('The browser blocked the artifact window.')
      }
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000)
    } catch (reason) {
      target?.close()
      setError((reason as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return <button type="button" aria-label={ariaLabel} title={error} disabled={busy} onClick={() => void open()}>
    {children}
  </button>
}

async function fetchArtifact(url: string, getToken: TokenProvider): Promise<Blob> {
  const token = await getToken()
  const response = await fetch(url, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  })
  if (!response.ok)
    throw new Error(`Artifact request failed (${response.status})`)
  return response.blob()
}
