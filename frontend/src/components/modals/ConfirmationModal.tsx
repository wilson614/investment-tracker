import { X, AlertCircle } from 'lucide-react';

interface ConfirmationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title: string;
  message: React.ReactNode;
  confirmText?: string;
  cancelText?: string;
  onCancel?: () => void;
  isDestructive?: boolean; // For red confirm button (e.g. delete)
}

export function ConfirmationModal({
  isOpen,
  onClose,
  onConfirm,
  title,
  message,
  confirmText = '確定',
  cancelText = '取消',
  onCancel,
  isDestructive = false,
}: ConfirmationModalProps) {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-[100]">
      <div className="card-dark p-0 w-full max-w-md m-4 shadow-xl border border-[var(--border-color)]">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-[var(--border-color)]">
          <div className="flex items-center gap-2">
            {isDestructive && <AlertCircle className="w-5 h-5 text-red-400" />}
            <h2 className="text-lg font-bold text-[var(--text-primary)]">
              {title}
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1 text-[var(--text-muted)] hover:text-[var(--text-primary)] rounded transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-5">
          <div className="text-[var(--text-secondary)] text-base">
            {message}
          </div>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 px-5 py-4 border-t border-[var(--border-color)] bg-[var(--bg-secondary)]/30">
          <button
            type="button"
            onClick={() => {
              if (onCancel) onCancel();
              onClose();
            }}
            className="btn-dark px-4 py-2"
          >
            {cancelText}
          </button>
          <button
            type="button"
            onClick={() => {
              onConfirm();
              onClose();
            }}
            className={isDestructive ? "btn-danger px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded transition-colors" : "btn-accent px-4 py-2"}
          >
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  );
}
