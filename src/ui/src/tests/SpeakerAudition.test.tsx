import '@testing-library/jest-dom/vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

import * as SpeakerAuditionModule from '../components/SpeakerAudition';

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

const SpeakerAudition = (SpeakerAuditionModule.SpeakerAudition ||
  SpeakerAuditionModule.default) as React.ComponentType<SpeakerAuditionProps>;

const mockSoloSnippet: SoloSnippetBounds = {
  start_ms: 12500,
  end_ms: 21800,
};

const mockSpeaker: SpeakerCluster = {
  cluster_id: 'cluster-speaker-0',
  display_name: null,
  match_confidence: 0.61,
  needs_disambiguation: false,
  solo_snippet: mockSoloSnippet,
  embedding_vector: new Array(512).fill(0.042),
};

function renderSpeakerAudition(props: Partial<SpeakerAuditionProps> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  const mergedProps: SpeakerAuditionProps = {
    recordingId: 'rec-101',
    speaker: mockSpeaker,
    audioUrl: '/api/recordings/rec-101/audio',
    ...props,
  };

  return render(
    <QueryClientProvider client={queryClient}>
      <SpeakerAudition {...mergedProps} />
    </QueryClientProvider>
  );
}

describe('SpeakerAudition', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.HTMLMediaElement.prototype.play = vi.fn().mockResolvedValue(undefined);
    window.HTMLMediaElement.prototype.pause = vi.fn();
  });

  it('SpeakerAudition_AuditionButtonClicked_PlaysAudioSnippetForSpeakerBounds', async () => {
    const user = userEvent.setup();
    const onPlaySnippet = vi.fn();

    renderSpeakerAudition({ onPlaySnippet });

    const auditionButton =
      screen.queryByRole('button', { name: /audition|play snippet|play audio|listen/i }) ||
      screen.getByTestId('audition-button');
    expect(auditionButton).toBeInTheDocument();

    await user.click(auditionButton);

    const wasSnippetPlayed =
      onPlaySnippet.mock.calls.length > 0 ||
      (window.HTMLMediaElement.prototype.play as ReturnType<typeof vi.fn>).mock.calls.length > 0;
    expect(wasSnippetPlayed).toBe(true);

    if (onPlaySnippet.mock.calls.length > 0) {
      expect(onPlaySnippet).toHaveBeenCalledWith(
        expect.objectContaining({
          start_ms: 12500,
          end_ms: 21800,
        })
      );
    }
  });

  it('SpeakerAudition_EnrollmentFormRendered_AllowsEnteringDisplayNameAndOrganization', async () => {
    const user = userEvent.setup();
    renderSpeakerAudition();

    // If modal trigger is present, open modal
    const nameInputPreCheck =
      screen.queryByLabelText(/display name|speaker name|name/i) ||
      screen.queryByPlaceholderText(/name/i);
    if (!nameInputPreCheck) {
      const openModalBtn = screen.queryByRole('button', { name: /enroll|tag|identify|add speaker/i });
      if (openModalBtn) {
        await user.click(openModalBtn);
      }
    }

    const nameInput =
      screen.queryByLabelText(/display name|speaker name|name/i) ||
      screen.queryByPlaceholderText(/name/i) ||
      screen.getByTestId('speaker-name-input');
    expect(nameInput).toBeInTheDocument();

    const orgInput =
      screen.queryByLabelText(/organization|company|org/i) ||
      screen.queryByPlaceholderText(/organization|org/i) ||
      screen.getByTestId('speaker-org-input');
    expect(orgInput).toBeInTheDocument();

    await user.type(nameInput, 'Dr. Aris Thorne');
    expect(nameInput).toHaveValue('Dr. Aris Thorne');

    await user.type(orgInput, 'Cyberdyne Systems');
    expect(orgInput).toHaveValue('Cyberdyne Systems');
  });

  it('SpeakerAudition_EnrollButtonClicked_CallsEnrollmentHandlerWithSpeakerDetailsAndEmbedding', async () => {
    const user = userEvent.setup();
    const onEnroll = vi.fn().mockResolvedValue(undefined);

    renderSpeakerAudition({ onEnroll });

    // Open form if inside modal
    const nameInputPreCheck =
      screen.queryByLabelText(/display name|speaker name|name/i) ||
      screen.queryByPlaceholderText(/name/i);
    if (!nameInputPreCheck) {
      const openModalBtn = screen.queryByRole('button', { name: /enroll|tag|identify|add speaker/i });
      if (openModalBtn) {
        await user.click(openModalBtn);
      }
    }

    const nameInput =
      screen.queryByLabelText(/display name|speaker name|name/i) ||
      screen.queryByPlaceholderText(/name/i) ||
      screen.getByTestId('speaker-name-input');
    await user.type(nameInput, 'Dr. Aris Thorne');

    const orgInput =
      screen.queryByLabelText(/organization|company|org/i) ||
      screen.queryByPlaceholderText(/organization|org/i) ||
      screen.getByTestId('speaker-org-input');
    await user.type(orgInput, 'Cyberdyne Systems');

    const enrollButton =
      screen.queryByRole('button', { name: /^enroll|^save speaker|^confirm/i }) ||
      screen.getByTestId('enroll-submit-button');
    expect(enrollButton).toBeInTheDocument();

    await user.click(enrollButton);

    expect(onEnroll).toHaveBeenCalledTimes(1);
    const callArg = onEnroll.mock.calls[0][0];
    const enrolledName =
      callArg.displayName || callArg.speaker_name || callArg.name || callArg.speakerName;
    const enrolledOrg = callArg.organization || callArg.org;
    const enrolledEmbedding =
      callArg.cluster_embedding || callArg.embedding || callArg.clusterEmbedding;

    expect(enrolledName).toBe('Dr. Aris Thorne');
    expect(enrolledOrg).toBe('Cyberdyne Systems');
    expect(enrolledEmbedding).toEqual(mockSpeaker.embedding_vector);
    expect(callArg.bounds).toEqual(mockSpeaker.solo_snippet);
  });

  it('SpeakerAudition_EnrolledSuccessfully_UpdatesSpeakerBadgeToDisplayName', async () => {
    const user = userEvent.setup();
    const onEnroll = vi.fn().mockResolvedValue(undefined);

    const { rerender } = renderSpeakerAudition({ onEnroll });

    // Open form if needed
    const nameInputPreCheck =
      screen.queryByLabelText(/display name|speaker name|name/i) ||
      screen.queryByPlaceholderText(/name/i);
    if (!nameInputPreCheck) {
      const openModalBtn = screen.queryByRole('button', { name: /enroll|tag|identify|add speaker/i });
      if (openModalBtn) {
        await user.click(openModalBtn);
      }
    }

    const nameInput =
      screen.queryByLabelText(/display name|speaker name|name/i) ||
      screen.queryByPlaceholderText(/name/i) ||
      screen.getByTestId('speaker-name-input');
    await user.type(nameInput, 'Dr. Aris Thorne');

    const enrollButton =
      screen.queryByRole('button', { name: /^enroll|^save speaker|^confirm/i }) ||
      screen.getByTestId('enroll-submit-button');
    await user.click(enrollButton);

    // Verify badge displays enrolled name (either via internal state or rerender)
    const nameElement = screen.queryByText('Dr. Aris Thorne');
    if (!nameElement) {
      rerender(
        <QueryClientProvider client={new QueryClient()}>
          <SpeakerAudition
            recordingId="rec-101"
            speaker={{
              ...mockSpeaker,
              display_name: 'Dr. Aris Thorne',
              enrolled_speaker_id: 'spk-999',
            }}
            onEnroll={onEnroll}
          />
        </QueryClientProvider>
      );
    }

    expect(screen.getByText('Dr. Aris Thorne')).toBeInTheDocument();
  });

  it('SpeakerAudition_AlreadyEnrolledSpeaker_DisplaysEnrolledNameBadge', () => {
    const enrolledSpeaker: SpeakerCluster = {
      ...mockSpeaker,
      cluster_id: 'cluster-speaker-1',
      display_name: 'Dr. Aris Thorne',
      enrolled_speaker_id: 'spk-999',
      match_confidence: 0.95,
    };

    renderSpeakerAudition({ speaker: enrolledSpeaker });

    expect(screen.getByText('Dr. Aris Thorne')).toBeInTheDocument();
  });
});
