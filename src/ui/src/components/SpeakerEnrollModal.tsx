import React, { useState } from 'react';
import { X, User, Building, AlertCircle } from 'lucide-react';

export interface SoloSnippetBounds {
  start_ms: number;
  end_ms: number;
}

export interface SpeakerCluster {
  cluster_id: string;
  enrolled_speaker_id?: string | null;
  display_name?: string | null;
  match_confidence?: number;
  needs_disambiguation?: boolean;
  solo_snippet: SoloSnippetBounds;
  embedding_vector?: number[];
}

export interface EnrollSpeakerPayload {
  speaker_name?: string;
  displayName?: string;
  name?: string;
  speakerName?: string;
  organization?: string;
  org?: string;
  cluster_embedding?: number[];
  embedding?: number[];
  clusterEmbedding?: number[];
  cluster_id?: string;
  clusterId?: string;
  recording_id?: string;
  recordingId?: string;
  bounds: { start_ms: number; end_ms: number };
}

export interface SpeakerEnrollModalProps {
  isOpen: boolean;
  onClose: () => void;
  speaker: SpeakerCluster;
  recordingId: string;
  bounds?: SoloSnippetBounds;
  onEnroll?: (payload: EnrollSpeakerPayload) => Promise<void> | void;
  onSuccess?: (displayName: string) => void;
  className?: string;
}

export const SpeakerEnrollModal: React.FC<SpeakerEnrollModalProps> = ({
  isOpen,
  onClose,
  speaker,
  recordingId,
  bounds: propsBounds,
  onEnroll,
  onSuccess,
  className = '',
}) => {
  const [displayName, setDisplayName] = useState<string>(speaker.display_name ?? '');
  const [organization, setOrganization] = useState<string>('');
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  if (!isOpen) {
    return null;
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmedName = displayName.trim();
    if (!trimmedName) {
      setErrorMessage('Display name is required');
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    const resolvedBounds = propsBounds ?? speaker.solo_snippet ?? { start_ms: 0, end_ms: 0 };

    const payload: EnrollSpeakerPayload = {
      speaker_name: trimmedName,
      displayName: trimmedName,
      name: trimmedName,
      speakerName: trimmedName,
      organization: organization.trim(),
      org: organization.trim(),
      cluster_embedding: speaker.embedding_vector,
      embedding: speaker.embedding_vector,
      clusterEmbedding: speaker.embedding_vector,
      cluster_id: speaker.cluster_id,
      clusterId: speaker.cluster_id,
      recording_id: recordingId,
      recordingId: recordingId,
      bounds: resolvedBounds,
    };

    try {
      if (onEnroll) {
        await onEnroll(payload);
      } else {
        const response = await fetch('/api/speakers/enroll', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        });
        if (!response.ok) {
          throw new Error(`Enrollment failed with status ${response.status}`);
        }
      }

      onSuccess?.(trimmedName);
      onClose();
    } catch (err: unknown) {
      setErrorMessage(
        err instanceof Error ? err.message : 'An error occurred while enrolling speaker.'
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="speaker-enroll-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm"
    >
      <div
        className={`bg-slate-900 border border-slate-800 rounded-xl max-w-md w-full p-6 shadow-2xl flex flex-col gap-5 text-slate-100 ${className}`}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-slate-800 pb-3">
          <div className="flex items-center gap-2">
            <User className="w-5 h-5 text-blue-400" />
            <h2 id="speaker-enroll-title" className="text-lg font-semibold text-white">
              Enroll Speaker Voiceprint
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="text-slate-400 hover:text-white p-1 rounded-lg hover:bg-slate-800 transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Speaker cluster info */}
        <div className="text-xs text-slate-400 bg-slate-950/60 p-3 rounded-lg border border-slate-800/80 flex flex-col gap-1">
          <div className="flex justify-between">
            <span className="text-slate-500">Cluster ID:</span>
            <span className="font-mono text-slate-300">{speaker.cluster_id}</span>
          </div>
          {speaker.solo_snippet && (
            <div className="flex justify-between">
              <span className="text-slate-500">Solo Snippet:</span>
              <span className="font-mono text-slate-300">
                {(speaker.solo_snippet.start_ms / 1000).toFixed(1)}s –{' '}
                {(speaker.solo_snippet.end_ms / 1000).toFixed(1)}s
              </span>
            </div>
          )}
        </div>

        {/* Error notification */}
        {errorMessage && (
          <div className="flex items-center gap-2 text-xs bg-red-950/60 border border-red-800 text-red-300 p-3 rounded-lg">
            <AlertCircle className="w-4 h-4 text-red-400 shrink-0" />
            <span>{errorMessage}</span>
          </div>
        )}

        {/* Form */}
        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <label
              htmlFor="speaker-name-input"
              className="text-xs font-medium text-slate-300 flex items-center gap-1.5"
            >
              <User className="w-3.5 h-3.5 text-slate-400" />
              <span>Display Name</span>
              <span className="text-red-400">*</span>
            </label>
            <input
              id="speaker-name-input"
              data-testid="speaker-name-input"
              aria-label="Display Name"
              type="text"
              placeholder="e.g. Dr. Aris Thorne"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              className="bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-slate-100 placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
              required
              autoFocus
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label
              htmlFor="speaker-org-input"
              className="text-xs font-medium text-slate-300 flex items-center gap-1.5"
            >
              <Building className="w-3.5 h-3.5 text-slate-400" />
              <span>Organization</span>
              <span className="text-slate-500 text-[10px]">(Optional)</span>
            </label>
            <input
              id="speaker-org-input"
              data-testid="speaker-org-input"
              aria-label="Organization"
              type="text"
              placeholder="e.g. Cyberdyne Systems"
              value={organization}
              onChange={(e) => setOrganization(e.target.value)}
              className="bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-slate-100 placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>

          {/* Action buttons */}
          <div className="flex items-center justify-end gap-3 pt-3 border-t border-slate-800">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="px-4 py-2 text-sm font-medium text-slate-300 hover:text-white rounded-lg hover:bg-slate-800 transition disabled:opacity-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              data-testid="enroll-submit-button"
              disabled={isSubmitting || !displayName.trim()}
              className="px-4 py-2 text-sm font-medium bg-blue-600 hover:bg-blue-500 text-white rounded-lg transition disabled:opacity-50 disabled:cursor-not-allowed shadow-md shadow-blue-900/30"
            >
              {isSubmitting ? 'Enrolling...' : 'Enroll Speaker'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default SpeakerEnrollModal;
