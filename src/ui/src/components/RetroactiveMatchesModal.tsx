import React, { useState, useEffect } from 'react';
import {
  Volume2,
  Check,
  X,
  ArrowRight,
  Folder,
  FolderSymlink,
  Loader2,
  UserCheck,
} from 'lucide-react';
import { MatchNotificationBanner } from './MatchNotificationBanner';

export interface RetroactiveMatch {
  id: string;
  recordingId: string;
  recordingTitle: string;
  speakerId: string;
  speakerName: string;
  confidence: number;
  currentFolder: string;
  projectedFolder: string;
  snippetUrl?: string;
  snippetDuration?: string;
}

export interface RetroactiveMatchesModalProps {
  isOpen?: boolean;
  onClose?: () => void;
  onOpen?: () => void;
  matches?: RetroactiveMatch[];
  onApprove?: (matchIds: string[]) => Promise<void> | void;
  onReject?: (matchId: string) => Promise<void> | void;
  onAuditionSnippet?: (match: RetroactiveMatch) => void;
  apiUrl?: string;
  className?: string;
  hideBanner?: boolean;
}

export const RetroactiveMatchesModal: React.FC<RetroactiveMatchesModalProps> = ({
  isOpen,
  onClose,
  onOpen,
  matches = [],
  onApprove,
  onReject,
  onAuditionSnippet,
  apiUrl = '',
  className = '',
  hideBanner = false,
}) => {
  const [items, setItems] = useState<RetroactiveMatch[]>(matches);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(
    () => new Set(matches.map((m) => m.id))
  );
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    setItems(matches);
    setSelectedIds(new Set(matches.map((m) => m.id)));
  }, [matches]);

  const effectiveIsOpen = isOpen !== undefined ? isOpen : isModalOpen;

  const handleOpen = () => {
    setIsModalOpen(true);
    onOpen?.();
  };

  const handleClose = () => {
    setIsModalOpen(false);
    onClose?.();
  };

  const toggleSelect = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selectedIds.size === items.length) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(items.map((m) => m.id)));
    }
  };

  const handleAudition = (match: RetroactiveMatch) => {
    if (onAuditionSnippet) {
      onAuditionSnippet(match);
    } else if (match.snippetUrl) {
      try {
        const audio = new Audio(match.snippetUrl);
        audio.play().catch(() => {});
      } catch {
        // audio playback might not be supported in test or headless environment
      }
    }
  };

  const handleReject = async (matchId: string) => {
    // Remove rejected item from UI state immediately
    setItems((prev) => prev.filter((m) => m.id !== matchId));
    setSelectedIds((prev) => {
      const next = new Set(prev);
      next.delete(matchId);
      return next;
    });

    if (onReject) {
      await onReject(matchId);
    }

    try {
      const base = apiUrl.replace(/\/$/, '');
      const endpoint = base
        ? `${base}/api/speakers/retroactive-matches/${matchId}/reject`
        : `/api/speakers/retroactive-matches/${matchId}/reject`;
      await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ matchId }),
      });
    } catch {
      // ignore network errors in tests/offline
    }
  };

  const handleApprove = async () => {
    const idsToApprove = Array.from(selectedIds);
    if (idsToApprove.length === 0) return;

    setIsSubmitting(true);
    try {
      if (onApprove) {
        await onApprove(idsToApprove);
      }

      try {
        const base = apiUrl.replace(/\/$/, '');
        const endpoint = base
          ? `${base}/api/speakers/retroactive-matches/approve`
          : '/api/speakers/retroactive-matches/approve';
        await fetch(endpoint, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ matchIds: idsToApprove }),
        });
      } catch {
        // ignore network errors in tests/offline
      }

      setItems((prev) => prev.filter((m) => !selectedIds.has(m.id)));
      setSelectedIds(new Set());
      handleClose();
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <>
      {!effectiveIsOpen && items.length > 0 && !hideBanner && (
        <MatchNotificationBanner
          matches={items}
          count={items.length}
          onReview={handleOpen}
          className={className}
        />
      )}

      {effectiveIsOpen && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="retroactive-modal-title"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm"
        >
          <div
            className={`relative w-full max-w-4xl max-h-[90vh] bg-slate-900 border border-slate-800 rounded-2xl shadow-2xl flex flex-col overflow-hidden ${className}`}
          >
            {/* Modal Header */}
            <div className="flex items-center justify-between px-6 py-4 border-b border-slate-800">
              <div className="flex items-center gap-3">
                <div className="p-2 bg-indigo-500/10 border border-indigo-500/20 rounded-xl text-indigo-400">
                  <UserCheck className="w-5 h-5" />
                </div>
                <div>
                  <h2
                    id="retroactive-modal-title"
                    className="text-lg font-semibold text-slate-100 tracking-tight"
                  >
                    Review Retroactive Matches
                  </h2>
                  <p className="text-xs text-slate-400">
                    Confirmed speakers identified in earlier recordings. Review and approve destination folder moves.
                  </p>
                </div>
              </div>
              <button
                type="button"
                onClick={handleClose}
                aria-label="Close dialog"
                className="p-2 text-slate-400 hover:text-slate-200 hover:bg-slate-800 rounded-lg transition-colors cursor-pointer"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Modal Content */}
            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {items.length === 0 ? (
                <div className="py-12 text-center text-slate-400 text-sm">
                  No pending retroactive matches.
                </div>
              ) : (
                <div className="bg-slate-950/50 border border-slate-800/80 rounded-xl overflow-hidden shadow-inner">
                  <table className="w-full text-left border-collapse">
                    <thead>
                      <tr className="border-b border-slate-800 bg-slate-950/80 text-xs font-semibold text-slate-400 uppercase tracking-wider">
                        <th className="py-3 px-4 w-10">
                          <input
                            type="checkbox"
                            checked={selectedIds.size === items.length && items.length > 0}
                            onChange={toggleSelectAll}
                            aria-label="Select all matches"
                            className="w-4 h-4 rounded border-slate-700 bg-slate-800 text-indigo-500 focus:ring-indigo-500 focus:ring-offset-slate-900 cursor-pointer"
                          />
                        </th>
                        <th className="py-3 px-4">Recording</th>
                        <th className="py-3 px-4">Confidence</th>
                        <th className="py-3 px-4">Routing Path</th>
                        <th className="py-3 px-4">Snippet</th>
                        <th className="py-3 px-4 text-right">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800/60 text-sm">
                      {items.map((item) => {
                        const confidencePercent = Math.round(
                          item.confidence <= 1 ? item.confidence * 100 : item.confidence
                        );
                        return (
                          <tr
                            key={item.id}
                            className="hover:bg-slate-800/30 transition-colors"
                          >
                            {/* Selection Checkbox */}
                            <td className="py-3.5 px-4">
                              <input
                                type="checkbox"
                                checked={selectedIds.has(item.id)}
                                onChange={() => toggleSelect(item.id)}
                                aria-label={`Select ${item.recordingTitle}`}
                                className="w-4 h-4 rounded border-slate-700 bg-slate-800 text-indigo-500 focus:ring-indigo-500 focus:ring-offset-slate-900 cursor-pointer"
                              />
                            </td>

                            {/* Recording Title and Speaker */}
                            <td className="py-3.5 px-4">
                              <div className="font-medium text-slate-100">
                                {item.recordingTitle}
                              </div>
                              <div className="text-xs text-slate-400">
                                Speaker: <span className="text-slate-300">{item.speakerName}</span>
                              </div>
                            </td>

                            {/* Confidence */}
                            <td className="py-3.5 px-4 whitespace-nowrap">
                              <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-mono font-medium bg-emerald-950 text-emerald-400 border border-emerald-800">
                                {confidencePercent}%
                              </span>
                            </td>

                            {/* Routing Folders */}
                            <td className="py-3.5 px-4 text-xs">
                              <div className="space-y-1">
                                <div className="flex items-center gap-1.5 text-slate-400">
                                  <Folder className="w-3.5 h-3.5 text-slate-500 shrink-0" />
                                  <span className="font-mono">{item.currentFolder}</span>
                                </div>
                                <div className="flex items-center gap-1.5 text-emerald-400 font-medium">
                                  <ArrowRight className="w-3.5 h-3.5 text-slate-500 shrink-0" />
                                  <FolderSymlink className="w-3.5 h-3.5 text-emerald-500 shrink-0" />
                                  <span className="font-mono">{item.projectedFolder}</span>
                                </div>
                              </div>
                            </td>

                            {/* Snippet Audition */}
                            <td className="py-3.5 px-4 whitespace-nowrap">
                              <button
                                type="button"
                                onClick={() => handleAudition(item)}
                                data-testid={`audition-snippet-${item.id}`}
                                aria-label={`Audition snippet for ${item.recordingTitle}`}
                                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 rounded-lg transition-colors cursor-pointer"
                              >
                                <Volume2 className="w-3.5 h-3.5 text-sky-400 shrink-0" />
                                <span>Audition Snippet</span>
                                {item.snippetDuration && (
                                  <span className="text-[10px] text-slate-400 font-mono">
                                    ({item.snippetDuration})
                                  </span>
                                )}
                              </button>
                            </td>

                            {/* Reject Button */}
                            <td className="py-3.5 px-4 text-right whitespace-nowrap">
                              <button
                                type="button"
                                onClick={() => handleReject(item.id)}
                                className="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium text-rose-400 hover:text-rose-300 hover:bg-rose-950/40 border border-rose-900/60 rounded-lg transition-colors cursor-pointer"
                              >
                                <X className="w-3.5 h-3.5" />
                                <span>Reject</span>
                              </button>
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {/* Modal Footer */}
            <div className="flex items-center justify-between px-6 py-4 border-t border-slate-800 bg-slate-950/40">
              <div className="text-xs text-slate-400 font-medium">
                {selectedIds.size} of {items.length} selected
              </div>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={handleClose}
                  className="px-4 py-2 text-xs font-medium text-slate-300 hover:text-white bg-slate-800 hover:bg-slate-700 rounded-xl transition-colors cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleApprove}
                  disabled={selectedIds.size === 0 || isSubmitting}
                  className="inline-flex items-center gap-2 px-4 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 disabled:cursor-not-allowed text-white font-medium text-xs rounded-xl transition-colors shadow-lg shadow-emerald-950/50 cursor-pointer"
                >
                  {isSubmitting ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <Check className="w-3.5 h-3.5" />
                  )}
                  <span>Approve Selected Matches</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export { MatchNotificationBanner };
export default RetroactiveMatchesModal;
