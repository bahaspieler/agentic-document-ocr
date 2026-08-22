import { useEffect, useState } from 'react'

export function usePersistentDisclosure(key: string, initial = true) {
  const [open, setOpen] = useState(() => {
    const stored = localStorage.getItem(key)
    return stored === null ? initial : stored === 'true'
  })
  useEffect(() => localStorage.setItem(key, String(open)), [key, open])
  return [open, setOpen] as const
}
