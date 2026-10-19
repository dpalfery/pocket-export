import '@testing-library/jest-dom/vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import App, { sampleRecordings, sampleMatches } from './App';

describe('App Component', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('App_InitialRender_MountsDashboardAndBanner', () => {
    render(<App />);

    // Header and Branding
    expect(screen.getByText('Pocket Export')).toBeInTheDocument();
    expect(screen.getByText('Desktop Pipeline & Vault Synchronizer')).toBeInTheDocument();

    // Sync and Pipeline Status Badges
    const syncBadge = screen.getByTestId('sync-status-badge');
    expect(syncBadge).toBeInTheDocument();
    expect(syncBadge).toHaveTextContent('Sync:Ready');

    const pipelineBadge = screen.getByTestId('pipeline-status-badge');
    expect(pipelineBadge).toBeInTheDocument();
    expect(pipelineBadge).toHaveTextContent('Pipeline:Idle');

    // MatchNotificationBanner is mounted
    const banner = screen.getByTestId('retroactive-matches-banner');
    expect(banner).toBeInTheDocument();
    expect(screen.getByText(/pending retroactive speaker matches found/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /review now/i })).toBeInTheDocument();

    // RecordingsDashboard is mounted
    expect(screen.getByPlaceholderText(/search recordings by title, transcript/i)).toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByText(sampleRecordings[0].title)).toBeInTheDocument();
  });

  it('App_ClickBannerReviewNow_OpensRetroactiveMatchesModal', async () => {
    const user = userEvent.setup();
    render(<App />);

    // Before click: modal dialog is not in document
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // Click "Review Now" on banner
    const reviewBtn = screen.getByRole('button', { name: /review now/i });
    await user.click(reviewBtn);

    // After click: RetroactiveMatchesModal is open
    const modal = screen.getByRole('dialog');
    expect(modal).toBeInTheDocument();
    expect(screen.getByText('Review Retroactive Matches')).toBeInTheDocument();
    expect(screen.getByText(sampleMatches[0].recordingTitle)).toBeInTheDocument();
  });

  it('App_CloseRetroactiveMatchesModal_ClosesModal', async () => {
    const user = userEvent.setup();
    render(<App />);

    // Open modal
    await user.click(screen.getByRole('button', { name: /review now/i }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    // Click cancel button in modal
    const cancelBtn = screen.getByRole('button', { name: /^cancel$/i });
    await user.click(cancelBtn);

    // Modal is closed
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('App_DismissBanner_HidesBanner', async () => {
    const user = userEvent.setup();
    render(<App />);

    const banner = screen.getByTestId('retroactive-matches-banner');
    expect(banner).toBeInTheDocument();

    const dismissBtn = screen.getByRole('button', { name: /dismiss banner/i });
    await user.click(dismissBtn);

    expect(screen.queryByTestId('retroactive-matches-banner')).not.toBeInTheDocument();
  });

  it('App_NoPendingMatches_DoesNotRenderBanner', () => {
    render(<App initialMatches={[]} />);

    expect(screen.queryByTestId('retroactive-matches-banner')).not.toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
  });
});
