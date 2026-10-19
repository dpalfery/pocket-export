import { useState } from 'react';
import { Mic, FolderSync, Activity, RefreshCw } from 'lucide-react';
import { RecordingsDashboard, type RecordingItem } from './components/RecordingsDashboard';
import { MatchNotificationBanner } from './components/MatchNotificationBanner';
import { RetroactiveMatchesModal, type RetroactiveMatch } from './components/RetroactiveMatchesModal';

export interface AppProps {
  initialRecordings?: RecordingItem[];
  initialMatches?: RetroactiveMatch[];
  initialSyncStatus?: string;
  initialPipelineStatus?: string;
}

export const sampleRecordings: RecordingItem[] = [
  {
    id: 'rec-1',
    date: '2026-10-18',
    title: 'Executive Architecture Review',
    duration: '24:15',
    domain: 'Work',
    customer: 'Acme Corp',
    project: 'Cloud Migration',
    speakers: ['Alice', 'Bob'],
    status: 'Completed',
    routedPath: 'routed/Work/Acme/Architecture-Review.md',
    transcript: 'Alice: Welcome everyone to the quarterly architecture review.',
    summary: 'Discussion covering cloud migration milestones and speaker diarization pipelines.',
  },
  {
    id: 'rec-2',
    date: '2026-10-17',
    title: 'Weekend Trail Planning',
    duration: '08:30',
    domain: 'Personal',
    speakers: ['David'],
    status: 'Completed',
    routedPath: 'routed/Personal/Trail-Planning.md',
    transcript: 'David: Planning the mountain trail route for Saturday.',
    summary: 'Review of packing lists and route options for the hike.',
  },
];

export const sampleMatches: RetroactiveMatch[] = [
  {
    id: 'match-1',
    recordingId: 'rec-101',
    recordingTitle: 'Client Strategy Session',
    speakerId: 'spk-bob',
    speakerName: 'Bob Smith',
    confidence: 0.88,
    currentFolder: 'routed/Work/Unsorted',
    projectedFolder: 'routed/Work/Clients/Acme',
    snippetDuration: '5.5s',
  },
  {
    id: 'match-2',
    recordingId: 'rec-102',
    recordingTitle: 'Project Kickoff Sync',
    speakerId: 'spk-bob',
    speakerName: 'Bob Smith',
    confidence: 0.79,
    currentFolder: 'routed/Personal/General',
    projectedFolder: 'routed/Work/Projects/Sync',
    snippetDuration: '6.0s',
  },
];

export default function App({
  initialRecordings = sampleRecordings,
  initialMatches = sampleMatches,
  initialSyncStatus = 'Ready',
  initialPipelineStatus = 'Idle',
}: AppProps = {}) {
  const [recordings] = useState<RecordingItem[]>(initialRecordings);
  const [matches, setMatches] = useState<RetroactiveMatch[]>(initialMatches);
  const [syncStatus, setSyncStatus] = useState<string>(initialSyncStatus);
  const [pipelineStatus] = useState<string>(initialPipelineStatus);
  const [isRetroactiveModalOpen, setIsRetroactiveModalOpen] = useState<boolean>(false);
  const [isBannerDismissed, setIsBannerDismissed] = useState<boolean>(false);
  const [isSyncing, setIsSyncing] = useState<boolean>(false);

  const handleSyncVault = async () => {
    setIsSyncing(true);
    setSyncStatus('Syncing');
    try {
      await fetch('/api/sync/now', { method: 'POST' });
    } catch {
      // tolerates offline / mocked backend
    } finally {
      setIsSyncing(false);
      setSyncStatus('Ready');
    }
  };

  const handleApproveMatches = async (approvedIds: string[]) => {
    setMatches((prev) => prev.filter((m) => !approvedIds.includes(m.id)));
  };

  const handleRejectMatch = async (matchId: string) => {
    setMatches((prev) => prev.filter((m) => m.id !== matchId));
  };

  return (
    <div className="min-h-screen flex flex-col bg-slate-950 text-slate-100 selection:bg-indigo-500/30">
      {/* Dark mode layout header */}
      <header className="border-b border-slate-800 bg-slate-900/90 backdrop-blur-md sticky top-0 z-40 px-6 py-4 shadow-md">
        <div className="max-w-7xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-indigo-950/80 border border-indigo-700/50 flex items-center justify-center shadow-inner">
              <Mic className="w-6 h-6 text-indigo-400" />
            </div>
            <div>
              <h1 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
                Pocket Export
              </h1>
              <p className="text-xs text-slate-400">Desktop Pipeline & Vault Synchronizer</p>
            </div>
          </div>

          <div className="flex items-center gap-3 flex-wrap">
            {/* Vault Sync Status Badge */}
            <div
              data-testid="sync-status-badge"
              className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full text-xs font-semibold bg-emerald-950 text-emerald-300 border border-emerald-800 shadow-sm"
            >
              <FolderSync className="w-3.5 h-3.5 text-emerald-400" />
              <span className="text-slate-400">Sync:</span>
              <span>{syncStatus}</span>
            </div>

            {/* Pipeline Status Badge */}
            <div
              data-testid="pipeline-status-badge"
              className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full text-xs font-semibold bg-indigo-950 text-indigo-300 border border-indigo-800 shadow-sm"
            >
              <Activity className="w-3.5 h-3.5 text-indigo-400" />
              <span className="text-slate-400">Pipeline:</span>
              <span>{pipelineStatus}</span>
            </div>

            {/* Sync Vault Action */}
            <button
              type="button"
              onClick={handleSyncVault}
              disabled={isSyncing}
              className="inline-flex items-center gap-2 px-3.5 py-1.5 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-500 active:bg-indigo-700 rounded-lg transition-colors shadow-sm shadow-indigo-600/30 cursor-pointer disabled:opacity-50"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? 'animate-spin' : ''}`} />
              <span>{isSyncing ? 'Syncing...' : 'Sync Vault'}</span>
            </button>
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl mx-auto w-full px-4 sm:px-6 lg:px-8 py-6 flex flex-col gap-6">
        {/* Match Notification Banner */}
        {!isBannerDismissed && matches.length > 0 && (
          <MatchNotificationBanner
            matches={matches}
            count={matches.length}
            onReview={() => setIsRetroactiveModalOpen(true)}
            onDismiss={() => setIsBannerDismissed(true)}
          />
        )}

        {/* Recordings Dashboard */}
        <section aria-label="Recordings Dashboard" className="flex-1">
          <RecordingsDashboard recordings={recordings} />
        </section>

        {/* Retroactive Matches Modal */}
        <RetroactiveMatchesModal
          isOpen={isRetroactiveModalOpen}
          onClose={() => setIsRetroactiveModalOpen(false)}
          matches={matches}
          hideBanner={true}
          onApprove={handleApproveMatches}
          onReject={handleRejectMatch}
        />
      </main>
    </div>
  );
}
