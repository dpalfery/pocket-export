import React from 'react';
import { Sparkles, ArrowRight, X } from 'lucide-react';
import type { RetroactiveMatch } from './RetroactiveMatchesModal';

export interface MatchNotificationBannerProps {
  count?: number;
  matches?: RetroactiveMatch[];
  onReview?: () => void;
  onDismiss?: () => void;
  className?: string;
}

export const MatchNotificationBanner: React.FC<MatchNotificationBannerProps> = ({
  count,
  matches,
  onReview,
  onDismiss,
  className = '',
}) => {
  const matchCount = count !== undefined ? count : matches?.length ?? 0;

  if (matchCount <= 0) {
    return null;
  }

  return (
    <aside
      role="alert"
      data-testid="retroactive-matches-banner"
      className={`bg-amber-950/70 border border-amber-800/80 rounded-xl p-4 flex items-center justify-between gap-4 text-amber-200 shadow-lg ${className}`}
    >
      <div className="flex items-center gap-3">
        <Sparkles className="w-5 h-5 text-amber-400 shrink-0" />
        <div>
          <div className="font-semibold text-amber-100 text-sm">
            {matchCount} pending retroactive speaker {matchCount === 1 ? 'match' : 'matches'} found
          </div>
          <p className="text-xs text-amber-300/80 mt-0.5">
            Voiceprint matches detected for earlier recordings. Review and approve moving them to the matching speaker&apos;s folder.
          </p>
        </div>
      </div>
      <div className="flex items-center gap-2 shrink-0">
        <button
          type="button"
          onClick={onReview}
          className="px-3.5 py-1.5 bg-amber-500 hover:bg-amber-400 text-slate-950 font-medium text-xs rounded-lg transition-colors shadow-sm flex items-center gap-1.5 cursor-pointer"
        >
          <span>Review Now</span>
          <ArrowRight className="w-3.5 h-3.5" />
        </button>
        {onDismiss && (
          <button
            type="button"
            onClick={onDismiss}
            aria-label="Dismiss banner"
            className="p-1.5 text-amber-400/80 hover:text-amber-200 hover:bg-amber-900/50 rounded-lg transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        )}
      </div>
    </aside>
  );
};

export default MatchNotificationBanner;
