import React, { useState, useRef, useEffect } from 'react';
import { Play, Pause, User, UserCheck, Sparkles, Volume2 } from 'lucide-react';
import { SpeakerEnrollModal } from './SpeakerEnrollModal';

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
  bounds?: SoloSnippetBounds;
}

export interface SpeakerAuditionProps {
  recordingId: string;
  speaker: SpeakerCluster;
  audioUrl?: string;
  onEnroll?: (payload: EnrollSpeakerPayload) => Promise<void> | void;
  onPlaySnippet?: (bounds: SoloSnippetBounds) => void;
  className?: string;
}

export const SpeakerAudition: React.FC<SpeakerAuditionProps> = ({
  recordingId,
  speaker,
  audioUrl,
  onEnroll,
  onPlaySnippet,
  className = '',
}) => {
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [isModalOpen, setIsModalOpen] = useState<boolean>(false);
  const [enrolledDisplayName, setEnrolledDisplayName] = useState<string | null>(
    speaker.display_name ?? null
  );

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const timerRef = useRef<NodeJS.Timeout | null>(null);

  useEffect(() => {
    if (speaker.display_name) {
      setEnrolledDisplayName(speaker.display_name);
    }
  }, [speaker.display_name]);

  useEffect(() => {
    return () => {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }
      if (audioRef.current) {
        audioRef.current.pause();
        audioRef.current = null;
      }
    };
  }, []);

  const handlePlaySnippet = async () => {
    if (onPlaySnippet) {
      onPlaySnippet(speaker.solo_snippet);
    }

    if (audioUrl) {
      try {
        if (!audioRef.current) {
          audioRef.current = new Audio(audioUrl);
        }
        const audio = audioRef.current;
        const startSec = (speaker.solo_snippet?.start_ms ?? 0) / 1000;
        const endSec = (speaker.solo_snippet?.end_ms ?? 0) / 1000;
        const durationSec = Math.max(0.1, endSec - startSec);

        audio.currentTime = startSec;
        setIsPlaying(true);
        await audio.play();

        if (timerRef.current) {
          clearTimeout(timerRef.current);
        }
        timerRef.current = setTimeout(() => {
          audio.pause();
          setIsPlaying(false);
        }, durationSec * 1000);
      } catch {
        // Fallback or headless environment where audio decoding is unsupported
        setIsPlaying(false);
      }
    }
  };

  const handleEnrollSuccess = (name: string) => {
    setEnrolledDisplayName(name);
    setIsModalOpen(false);
  };

  const activeName = speaker.display_name || enrolledDisplayName;
  const isEnrolled = Boolean(activeName);

  return (
    <div
      data-testid={`speaker-audition-${speaker.cluster_id}`}
      className={`bg-slate-900 border border-slate-800 rounded-xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4 text-slate-100 shadow-md ${className}`}
    >
      {/* Left side: speaker identity badge and metadata */}
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-full bg-slate-800 border border-slate-700 flex items-center justify-center shrink-0">
          {isEnrolled ? (
            <UserCheck className="w-5 h-5 text-emerald-400" />
          ) : (
            <User className="w-5 h-5 text-slate-400" />
          )}
        </div>

        <div className="flex flex-col gap-1">
          <div className="flex items-center gap-2 flex-wrap">
            {isEnrolled ? (
              <span
                data-testid="speaker-badge"
                className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-950 text-emerald-300 border border-emerald-800"
              >
                <Sparkles className="w-3 h-3 text-emerald-400" />
                <span>{activeName}</span>
              </span>
            ) : (
              <span
                data-testid="speaker-badge"
                className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-slate-800 text-slate-300 border border-slate-700 font-mono"
              >
                <span>{speaker.cluster_id}</span>
              </span>
            )}

            {speaker.match_confidence != null && (
              <span className="text-[11px] text-slate-400 font-mono">
                {Math.round(speaker.match_confidence * 100)}% match
              </span>
            )}

            {speaker.needs_disambiguation && (
              <span className="inline-flex px-1.5 py-0.5 rounded text-[10px] font-semibold bg-amber-950 text-amber-300 border border-amber-800">
                Disambiguation Required
              </span>
            )}
          </div>

          {speaker.solo_snippet && (
            <div className="text-xs text-slate-400 flex items-center gap-1.5">
              <Volume2 className="w-3.5 h-3.5 text-slate-500" />
              <span>Solo snippet:</span>
              <span className="font-mono text-slate-300">
                {(speaker.solo_snippet.start_ms / 1000).toFixed(1)}s –{' '}
                {(speaker.solo_snippet.end_ms / 1000).toFixed(1)}s
              </span>
            </div>
          )}
        </div>
      </div>

      {/* Right side: controls */}
      <div className="flex items-center gap-2 sm:self-center">
        <button
          type="button"
          onClick={handlePlaySnippet}
          data-testid="audition-button"
          aria-label={isPlaying ? 'Pause snippet' : 'Audition solo snippet'}
          className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-slate-200 bg-slate-800 hover:bg-slate-700 hover:text-white rounded-lg border border-slate-700 transition"
        >
          {isPlaying ? (
            <Pause className="w-3.5 h-3.5 text-blue-400" />
          ) : (
            <Play className="w-3.5 h-3.5 text-blue-400" />
          )}
          <span>Audition</span>
        </button>

        {/* Modal open trigger button (hidden when modal is open to avoid duplicate action buttons in DOM) */}
        {!isModalOpen && (
          <button
            type="button"
            onClick={() => setIsModalOpen(true)}
            data-testid="enroll-modal-trigger"
            aria-label="Enroll speaker"
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-blue-600 hover:bg-blue-500 rounded-lg transition shadow-sm shadow-blue-900/30"
          >
            <UserCheck className="w-3.5 h-3.5" />
            <span>Enroll</span>
          </button>
        )}
      </div>

      {/* Enrollment modal */}
      <SpeakerEnrollModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        speaker={speaker}
        recordingId={recordingId}
        onEnroll={onEnroll}
        onSuccess={handleEnrollSuccess}
      />
    </div>
  );
};

export default SpeakerAudition;
