import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChatAttachment } from '../api'

export type PendingAttachment = ChatAttachment & {
  id: string; file: File; preview: string; progress: number
  status: 'uploading' | 'ready' | 'error'; error?: string
}

export function useImageAttachments(getToken: () => Promise<string | undefined>) {
  const [items, setItems] = useState<PendingAttachment[]>([])
  const itemsRef = useRef(items)
  useEffect(() => { itemsRef.current = items }, [items])
  useEffect(() => () => itemsRef.current.forEach((item) => URL.revokeObjectURL(item.preview)), [])

  const addFiles = useCallback(async (files: File[]) => {
    const images = files.filter((file) => file.type.startsWith('image/'))
    for (const file of images) {
      const id = crypto.randomUUID()
      const item: PendingAttachment = {
        id, file, preview: URL.createObjectURL(file), progress: 0, status: 'uploading',
        reference: '', name: file.name, contentType: file.type,
      }
      setItems((current) => [...current, item])
      try {
        const token = await getToken()
        const reference = await uploadImage(file, token, (progress) =>
          setItems((current) => current.map((row) => row.id === id ? { ...row, progress } : row)))
        setItems((current) => current.map((row) =>
          row.id === id ? { ...row, reference, progress: 100, status: 'ready' } : row))
      } catch (error) {
        setItems((current) => current.map((row) =>
          row.id === id ? { ...row, status: 'error', error: (error as Error).message } : row))
      }
    }
  }, [getToken])

  const remove = useCallback((id: string) => setItems((current) => {
    const removed = current.find((item) => item.id === id)
    if (removed) URL.revokeObjectURL(removed.preview)
    return current.filter((item) => item.id !== id)
  }), [])
  const clear = useCallback(() => setItems((current) => {
    current.forEach((item) => URL.revokeObjectURL(item.preview))
    return []
  }), [])
  return { items, addFiles, remove, clear }
}

function uploadImage(file: File, token: string | undefined, onProgress: (value: number) => void) {
  return new Promise<string>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', '/api/images')
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress(Math.round(event.loaded / event.total * 100))
    }
    xhr.onerror = () => reject(new Error('Image upload failed.'))
    xhr.onload = () => {
      let payload: { reference?: string; error?: string } = {}
      try { payload = JSON.parse(xhr.responseText) as typeof payload } catch { /* handled below */ }
      if (xhr.status >= 200 && xhr.status < 300 && payload.reference) resolve(payload.reference)
      else reject(new Error(payload.error ?? `Image upload failed (${xhr.status}).`))
    }
    const form = new FormData()
    form.append('file', file)
    xhr.send(form)
  })
}
