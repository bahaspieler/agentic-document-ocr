import { ImagePlus, Paperclip, X } from 'lucide-react'
import { useRef } from 'react'
import type { PendingAttachment } from '../hooks/useImageAttachments'

export function AttachmentPicker({
  items, onAdd, onRemove,
}: {
  items: PendingAttachment[]
  onAdd: (files: File[]) => void
  onRemove: (id: string) => void
}) {
  const input = useRef<HTMLInputElement>(null)
  return <>
    {items.length > 0 && <div className="attachment-strip" aria-label="Image attachments">
      {items.map((item) => <figure key={item.id} className={item.status}>
        <img src={item.preview} alt="" />
        <figcaption>{item.name}<small>{item.status === 'uploading' ? `${item.progress}%` : item.status}</small></figcaption>
        <button type="button" aria-label={`Remove ${item.name}`} onClick={() => onRemove(item.id)}><X size={13} /></button>
        {item.status === 'uploading' && <i style={{ width: `${item.progress}%` }} />}
      </figure>)}
    </div>}
    <input ref={input} className="visually-hidden" type="file" accept="image/*" multiple
      onChange={(event) => { onAdd(Array.from(event.target.files ?? [])); event.target.value = '' }} />
    <button className="attach-button" type="button" aria-label="Attach images" onClick={() => input.current?.click()}>
      {items.length ? <ImagePlus size={18} /> : <Paperclip size={18} />}
    </button>
  </>
}
