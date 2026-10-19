import '@testing-library/jest-dom/vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

// Expected RED failure: Component does not exist yet in src/ui/src/components/RetroactiveMatchesModal.tsx
import * as RetroactiveMatchesModalModule from '../components/RetroactiveMatchesModal';

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
  matches?: RetroactiveMatch[];
  onApprove?: (matchIds: string[]) => Promise<void> | void;
  onReject?: (matchId: string) => Promise<void> | void;
  onAuditionSnippet?: (match: RetroactiveMatch) => void;
  apiUrl?: string;
}

const RetroactiveMatchesModal = (RetroactiveMatchesModalModule.RetroactiveMatchesModal ||
  RetroactiveMatchesModalModule.default) as React.ComponentType<RetroactiveMatchesModalProps>;

export const mockMatches: RetroactiveMatch[] = [
  {
    id: 'match-1',
    recordingId: 'rec-101',
    recordingTitle: 'Client Strategy Session',
    speakerId: 'spk-bob',
    speakerName: 'Bob Smith',
    confidence: 0.88,
    currentFolder: 'routed/Work/Unsorted',
    projectedFolder: 'routed/Work/Clients/Acme',
    snippetUrl: '/api/audio/rec-101/snippet?start=12000&end=17500',
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
    snippetUrl: '/api/audio/rec-102/snippet?start=45000&end=51000',
    snippetDuration: '6.0s',
  },
];

function renderModal(props: RetroactiveMatchesModalProps = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <RetroactiveMatchesModal matches={mockMatches} isOpen={true} {...props} />
    </QueryClientProvider>
  );
}

describe('RetroactiveMatchesModal', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('RetroactiveMatchesModal_PendingMatchesExist_DisplaysNotificationBanner', () => {
    renderModal({ isOpen: false, matches: mockMatches });

    // Banner displays when pending proposed retroactive speaker matches exist
    const banner =
      screen.queryByRole('banner') ||
      screen.queryByRole('alert') ||
      screen.queryByTestId('retroactive-matches-banner') ||
      screen.getByText(/retroactive speaker match|pending.*matches|review retroactive matches/i);

    expect(banner).toBeInTheDocument();
    expect(
      screen.getByText(/retroactive speaker match|pending.*matches|review retroactive matches/i)
    ).toBeInTheDocument();
  });

  it('RetroactiveMatchesModal_NoMatchesExist_HidesNotificationBanner', () => {
    renderModal({ isOpen: false, matches: [] });

    expect(
      screen.queryByText(/retroactive speaker match|pending.*matches|review retroactive matches/i)
    ).not.toBeInTheDocument();
  });

  it('RetroactiveMatchesModal_ModalOpened_DisplaysRecordingsWithConfidenceAuditionButtonAndFolderPaths', () => {
    renderModal({ isOpen: true, matches: mockMatches });

    // Displays suggested recordings
    expect(screen.getByText('Client Strategy Session')).toBeInTheDocument();
    expect(screen.getByText('Project Kickoff Sync')).toBeInTheDocument();

    // Displays match confidence
    expect(screen.getByText(/88%|0\.88/)).toBeInTheDocument();
    expect(screen.getByText(/79%|0\.79/)).toBeInTheDocument();

    // Displays current folder location
    expect(screen.getByText(/routed\/Work\/Unsorted/)).toBeInTheDocument();
    expect(screen.getByText(/routed\/Personal\/General/)).toBeInTheDocument();

    // Displays projected new destination folder path
    expect(screen.getByText(/routed\/Work\/Clients\/Acme/)).toBeInTheDocument();
    expect(screen.getByText(/routed\/Work\/Projects\/Sync/)).toBeInTheDocument();

    // Displays snippet audition button
    const auditionButtons =
      screen.queryAllByRole('button', { name: /audition|play snippet|listen|preview/i });
    const auditionTestIds = screen.queryAllByTestId(/audition-snippet/i);
    expect(auditionButtons.length + auditionTestIds.length).toBeGreaterThan(0);
  });

  it('RetroactiveMatchesModal_AuditionButtonClicked_TriggersSnippetPlayback', async () => {
    const user = userEvent.setup();
    const onAuditionSnippet = vi.fn();
    renderModal({ isOpen: true, matches: mockMatches, onAuditionSnippet });

    const auditionButton =
      screen.queryAllByRole('button', { name: /audition|play snippet|listen|preview/i })[0] ||
      screen.queryAllByTestId(/audition-snippet/i)[0];

    expect(auditionButton).toBeInTheDocument();
    await user.click(auditionButton);

    expect(onAuditionSnippet).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'match-1' })
    );
  });

  it('RetroactiveMatchesModal_ApproveSelectedMatchesClicked_DispatchesApprovalCommandToApi', async () => {
    const user = userEvent.setup();
    const onApprove = vi.fn();

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      return new Response(JSON.stringify({ status: 'ok', approved: ['match-1', 'match-2'] }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    });

    renderModal({ isOpen: true, matches: mockMatches, onApprove });

    const approveButton = screen.getByRole('button', {
      name: /approve selected matches|approve selected|approve matches/i,
    });
    expect(approveButton).toBeInTheDocument();

    await user.click(approveButton);

    // Either onApprove prop was invoked or fetch dispatched POST to API
    const onApproveCalled = onApprove.mock.calls.length > 0;
    const fetchCalled = fetchSpy.mock.calls.some(([url, options]) => {
      const urlStr = typeof url === 'string' ? url : (url as Request).url;
      return (
        urlStr.includes('/api/speakers/retroactive-matches/approve') ||
        urlStr.includes('/approve')
      ) && (!options || options.method === 'POST');
    });

    expect(onApproveCalled || fetchCalled).toBe(true);

    if (onApproveCalled) {
      expect(onApprove).toHaveBeenCalledWith(expect.arrayContaining(['match-1']));
    }
  });

  it('RetroactiveMatchesModal_RejectClicked_RecordsDismissalAndRemovesRowFromList', async () => {
    const user = userEvent.setup();
    const onReject = vi.fn();

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      return new Response(JSON.stringify({ status: 'ok', rejected: 'match-1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    });

    renderModal({ isOpen: true, matches: mockMatches, onReject });

    // Initially both recordings are present
    expect(screen.getByText('Client Strategy Session')).toBeInTheDocument();
    expect(screen.getByText('Project Kickoff Sync')).toBeInTheDocument();

    // Find reject button for the first row
    const rejectButtons = screen.getAllByRole('button', { name: /reject|dismiss|ignore/i });
    expect(rejectButtons.length).toBeGreaterThan(0);

    await user.click(rejectButtons[0]);

    // Either onReject was called or API dismissal was dispatched
    const onRejectCalled = onReject.mock.calls.length > 0;
    const fetchCalled = fetchSpy.mock.calls.some(([url]) => {
      const urlStr = typeof url === 'string' ? url : (url as Request).url;
      return urlStr.includes('/api/speakers/retroactive-matches') || urlStr.includes('/reject');
    });

    expect(onRejectCalled || fetchCalled).toBe(true);
    if (onRejectCalled) {
      expect(onReject).toHaveBeenCalledWith('match-1');
    }

    // Row should be removed from the list, while the second remains
    expect(screen.queryByText('Client Strategy Session')).not.toBeInTheDocument();
    expect(screen.getByText('Project Kickoff Sync')).toBeInTheDocument();
  });
});
