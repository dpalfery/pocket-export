import React, { useState } from 'react';
import { X, Cpu, Sliders, Check } from 'lucide-react';

export interface ReTranscribeDialogProps {
  isOpen: boolean;
  recordingId?: string;
  onClose?: () => void;
  onConfirm?: (model: string) => Promise<void> | void;
  onReTranscribe?: (options: { model: string; recordingId?: string; beamSize?: number }) => Promise<void> | void;
  apiUrl?: string;
  className?: string;
}

export const ReTranscribeDialog: React.FC<ReTranscribeDialogProps> = ({
  isOpen,
  recordingId,
  onClose,
  onConfirm,
  onReTranscribe,
  apiUrl,
  className = '',
}) => {
  const [selectedModel, setSelectedModel] = useState<string>('whisper-large-v3-turbo');
  const [beamSize, setBeamSize] = useState<number>(5);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) {
    return null;
  }

  const handleConfirm = async () => {
    setIsSubmitting(true);
    setError(null);
    try {
      if (onConfirm) {
        await onConfirm(selectedModel);
      }
      if (onReTranscribe) {
        await onReTranscribe({ model: selectedModel, recordingId, beamSize });
      }
      if (!onConfirm && !onReTranscribe) {
        const base = apiUrl ? apiUrl.replace(/\/$/, '') : '';
        const res = await fetch(`${base}/api/recordings/${recordingId || ''}/retranscribe`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ model: selectedModel, recordingId, beamSize }),
        });
        if (!res.ok) {
          throw new Error(`Re-transcription failed with status ${res.status}`);
        }
      }
      onClose?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to trigger re-transcription');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="retranscribe-dialog-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm"
    >
      <div
        className={`bg-slate-900 border border-slate-800 rounded-xl max-w-md w-full p-6 shadow-2xl text-slate-100 flex flex-col gap-5 ${className}`}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-slate-800 pb-3">
          <div className="flex items-center gap-2">
            <Cpu className="w-5 h-5 text-indigo-400" />
            <h3 id="retranscribe-dialog-title" className="text-base font-semibold text-white">
              Re-Transcribe Recording
            </h3>
          </div>
          {onClose && (
            <button
              type="button"
              onClick={onClose}
              aria-label="Close dialog"
              className="text-slate-400 hover:text-white p-1 rounded-lg hover:bg-slate-800 transition"
            >
              <X className="w-5 h-5" />
            </button>
          )}
        </div>

        {error && (
          <div className="p-3 bg-rose-950/40 border border-rose-800/60 rounded-lg text-rose-300 text-xs">
            {error}
          </div>
        )}

        {/* Model Selection */}
        <div className="space-y-3">
          <label className="block text-xs font-semibold text-slate-400 uppercase tracking-wider">
            Transcription Model
          </label>
          <div role="radiogroup" aria-label="Transcription Model" className="space-y-2">
            <label
              htmlFor="model-whisper"
              className={`flex items-start gap-3 p-3 rounded-lg border cursor-pointer transition ${
                selectedModel === 'whisper-large-v3-turbo'
                  ? 'border-indigo-500 bg-indigo-950/30'
                  : 'border-slate-800 bg-slate-950 hover:bg-slate-800/50'
              }`}
            >
              <input
                type="radio"
                id="model-whisper"
                name="model"
                value="whisper-large-v3-turbo"
                aria-label="whisper-large-v3-turbo"
                checked={selectedModel === 'whisper-large-v3-turbo'}
                onChange={() => setSelectedModel('whisper-large-v3-turbo')}
                className="mt-0.5 text-indigo-600 focus:ring-indigo-500"
              />
              <div className="flex-1">
                <span className="text-sm font-medium text-slate-200 block">whisper-large-v3-turbo</span>
                <span className="text-xs text-slate-400 block mt-0.5">
                  Faster-Whisper / MLX on Apple Silicon. Fast, robust, multilingual.
                </span>
              </div>
            </label>

            <label
              htmlFor="model-parakeet"
              className={`flex items-start gap-3 p-3 rounded-lg border cursor-pointer transition ${
                selectedModel === 'parakeet-tdt-0.6b-v2'
                  ? 'border-indigo-500 bg-indigo-950/30'
                  : 'border-slate-800 bg-slate-950 hover:bg-slate-800/50'
              }`}
            >
              <input
                type="radio"
                id="model-parakeet"
                name="model"
                value="parakeet-tdt-0.6b-v2"
                aria-label="parakeet-tdt-0.6b-v2"
                checked={selectedModel === 'parakeet-tdt-0.6b-v2'}
                onChange={() => setSelectedModel('parakeet-tdt-0.6b-v2')}
                className="mt-0.5 text-indigo-600 focus:ring-indigo-500"
              />
              <div className="flex-1">
                <span className="text-sm font-medium text-slate-200 block">parakeet-tdt-0.6b-v2</span>
                <span className="text-xs text-slate-400 block mt-0.5">
                  NVIDIA FastConformer Parakeet TDT 0.6B (CUDA). High throughput on RTX.
                </span>
              </div>
            </label>
          </div>
        </div>

        {/* Beam Size */}
        <div>
          <label htmlFor="beam-size" className="flex items-center gap-1.5 text-xs font-semibold text-slate-400 uppercase tracking-wider mb-1.5">
            <Sliders className="w-3.5 h-3.5 text-slate-400" />
            Beam Size
          </label>
          <select
            id="beam-size"
            aria-label="Beam Size"
            value={beamSize}
            onChange={(e) => setBeamSize(Number(e.target.value))}
            className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value={1}>1 (Greedy / Fastest)</option>
            <option value={3}>3 (Balanced)</option>
            <option value={5}>5 (Accurate / Recommended)</option>
          </select>
        </div>

        {/* Footer Actions */}
        <div className="flex items-center justify-end gap-3 pt-2 border-t border-slate-800">
          {onClose && (
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="px-4 py-2 rounded-lg text-sm font-medium text-slate-400 hover:text-white hover:bg-slate-800 transition"
            >
              Cancel
            </button>
          )}
          <button
            type="button"
            data-testid="retranscribe-confirm-button"
            onClick={handleConfirm}
            disabled={isSubmitting}
            className="px-4 py-2 rounded-lg text-sm font-medium bg-indigo-600 hover:bg-indigo-500 text-white transition flex items-center gap-2 disabled:opacity-50"
          >
            <Check className="w-4 h-4" />
            Confirm Re-Transcribe
          </button>
        </div>
      </div>
    </div>
  );
};

export default ReTranscribeDialog;
