import '@testing-library/jest-dom/vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

// Expected RED failure: Component does not exist yet in src/ui/src/components/RecordingsDashboard.tsx
import * as RecordingsDashboardModule from '../components/RecordingsDashboard';

export interface RecordingItem {
  id: string;
  date: string;
  title: string;
  duration: string;
  domain: 'Work' | 'Personal';
  customer?: string;
  project?: string;
  speakers: string[];
  status: string;
  routedPath: string;
  transcript?: string;
  activeStage?: 'Transcribing' | 'Diarizing' | 'Classifying' | 'Summarizing';
  metadata?: Record<string, unknown>;
}

export interface RecordingsDashboardProps {
  recordings?: RecordingItem[];
  onSelectRecording?: (recording: RecordingItem) => void;
}

const RecordingsDashboard = (RecordingsDashboardModule.RecordingsDashboard ||
  RecordingsDashboardModule.default) as React.ComponentType<RecordingsDashboardProps>;

const mockRecordings: RecordingItem[] = [
  {
    id: 'rec-1',
    date: '2026-10-18',
    title: 'Q3 Budget Review',
    duration: '14:22',
    domain: 'Work',
    customer: 'Acme Corp',
    project: 'Finance 2026',
    speakers: ['Alice', 'Bob'],
    status: 'Completed',
    routedPath: '/vault/routed/work/acme/q3-budget.md',
    transcript: 'Alice: Let us discuss the quarterly budget figures.',
    metadata: {
      model: 'whisper-large-v3-turbo',
      audioPath: '/vault/staging/rec-1.m4a',
    },
  },
  {
    id: 'rec-2',
    date: '2026-10-17',
    title: 'Weekend Hiking Plans',
    duration: '05:40',
    domain: 'Personal',
    customer: 'N/A',
    project: 'Outdoors',
    speakers: ['Dave', 'Sarah'],
    status: 'Completed',
    routedPath: '/vault/routed/personal/hiking-plans.md',
    transcript: 'Dave: The weather looks great for Mount Rainier this Saturday.',
    metadata: {
      model: 'parakeet-tdt-0.6b-v2',
      audioPath: '/vault/staging/rec-2.m4a',
    },
  },
  {
    id: 'rec-3',
    date: '2026-10-18',
    title: 'Architecture Sync',
    duration: '22:15',
    domain: 'Work',
    customer: 'Internal',
    project: 'Pipeline V2',
    speakers: ['Alice', 'Dave'],
    status: 'Processing',
    routedPath: '/vault/routed/work/pipeline.md',
    transcript: 'In progress transcription...',
    activeStage: 'Transcribing',
    metadata: {
      model: 'whisper-large-v3-turbo',
      audioPath: '/vault/staging/rec-3.m4a',
    },
  },
  {
    id: 'rec-4',
    date: '2026-10-18',
    title: 'Client Call',
    duration: '30:00',
    domain: 'Work',
    customer: 'Globex',
    project: 'Expansion',
    speakers: ['Bob'],
    status: 'Processing',
    routedPath: '/vault/routed/work/globex.md',
    transcript: 'Diarizing speakers...',
    activeStage: 'Diarizing',
    metadata: {
      model: 'whisper-large-v3-turbo',
      audioPath: '/vault/staging/rec-4.m4a',
    },
  },
  {
    id: 'rec-5',
    date: '2026-10-18',
    title: 'Taxonomy Triage',
    duration: '11:05',
    domain: 'Work',
    customer: 'Internal',
    project: 'Taxonomy',
    speakers: ['Alice'],
    status: 'Processing',
    routedPath: '/vault/routed/work/taxonomy.md',
    transcript: 'Classifying notes...',
    activeStage: 'Classifying',
    metadata: {
      model: 'whisper-large-v3-turbo',
      audioPath: '/vault/staging/rec-5.m4a',
    },
  },
  {
    id: 'rec-6',
    date: '2026-10-18',
    title: 'Daily Standup',
    duration: '08:12',
    domain: 'Work',
    customer: 'Internal',
    project: 'Core',
    speakers: ['Dave', 'Bob'],
    status: 'Processing',
    routedPath: '/vault/routed/work/standup.md',
    transcript: 'Generating summary...',
    activeStage: 'Summarizing',
    metadata: {
      model: 'whisper-large-v3-turbo',
      audioPath: '/vault/staging/rec-6.m4a',
    },
  },
];

function renderDashboard(props: RecordingsDashboardProps = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <RecordingsDashboard recordings={mockRecordings} {...props} />
    </QueryClientProvider>
  );
}

describe('RecordingsDashboard', () => {
  it('RecordingsDashboard_InitialRender_RendersTableWithRequiredColumns', () => {
    renderDashboard();

    const expectedHeaders = [
      'Date',
      'Title',
      'Duration',
      'Domain',
      'Customer',
      'Project',
      'Speakers',
      'Status',
      'Routed Path',
    ];

    for (const header of expectedHeaders) {
      const headerEl =
        screen.queryByRole('columnheader', { name: new RegExp(header, 'i') }) ||
        screen.getByText(new RegExp(`^${header}$`, 'i'));
      expect(headerEl).toBeInTheDocument();
    }

    expect(screen.getByText('Q3 Budget Review')).toBeInTheDocument();
    expect(screen.getByText('Weekend Hiking Plans')).toBeInTheDocument();
  });

  it('RecordingsDashboard_SearchInputEntered_FiltersRowsByTitleOrText', async () => {
    const user = userEvent.setup();
    renderDashboard();

    const searchInput =
      screen.queryByRole('searchbox') ||
      screen.queryByPlaceholderText(/search/i) ||
      screen.getByLabelText(/search/i);
    expect(searchInput).toBeInTheDocument();

    // Filter by title
    await user.type(searchInput, 'Budget');
    expect(screen.getByText('Q3 Budget Review')).toBeInTheDocument();
    expect(screen.queryByText('Weekend Hiking Plans')).not.toBeInTheDocument();

    // Clear and filter by text content in transcript
    await user.clear(searchInput);
    await user.type(searchInput, 'Mount Rainier');
    expect(screen.getByText('Weekend Hiking Plans')).toBeInTheDocument();
    expect(screen.queryByText('Q3 Budget Review')).not.toBeInTheDocument();
  });

  it('RecordingsDashboard_DomainFacetSelected_FiltersBetweenWorkAndPersonal', async () => {
    const user = userEvent.setup();
    renderDashboard();

    const domainFilter =
      screen.queryByRole('combobox', { name: /domain/i }) ||
      screen.queryByLabelText(/domain/i) ||
      screen.getByTestId('domain-filter');
    expect(domainFilter).toBeInTheDocument();

    // Filter by Work
    if (domainFilter.tagName.toLowerCase() === 'select') {
      await user.selectOptions(domainFilter, 'Work');
    } else {
      await user.click(domainFilter);
      await user.click(screen.getByRole('option', { name: /work/i }));
    }
    expect(screen.getByText('Q3 Budget Review')).toBeInTheDocument();
    expect(screen.queryByText('Weekend Hiking Plans')).not.toBeInTheDocument();

    // Filter by Personal
    if (domainFilter.tagName.toLowerCase() === 'select') {
      await user.selectOptions(domainFilter, 'Personal');
    } else {
      await user.click(domainFilter);
      await user.click(screen.getByRole('option', { name: /personal/i }));
    }
    expect(screen.getByText('Weekend Hiking Plans')).toBeInTheDocument();
    expect(screen.queryByText('Q3 Budget Review')).not.toBeInTheDocument();
  });

  it('RecordingsDashboard_RowClicked_OpensDetailPaneWithTranscriptAndMetadata', async () => {
    const user = userEvent.setup();
    renderDashboard();

    // Select a recording row
    const rowItem = screen.getByText('Q3 Budget Review');
    await user.click(rowItem);

    // Detail pane displays full transcript
    expect(
      screen.getByText(/Alice: Let us discuss the quarterly budget figures/i)
    ).toBeInTheDocument();

    // Detail pane displays metadata
    expect(screen.getByText(/whisper-large-v3-turbo/i)).toBeInTheDocument();
    expect(screen.getByText(/\/vault\/staging\/rec-1\.m4a/i)).toBeInTheDocument();
  });

  it('RecordingsDashboard_ActiveStages_DisplaysLivePipelineProgressIndicators', () => {
    renderDashboard();

    const activeStages = ['Transcribing', 'Diarizing', 'Classifying', 'Summarizing'];

    for (const stage of activeStages) {
      expect(screen.getByText(new RegExp(stage, 'i'))).toBeInTheDocument();
    }
  });
});
