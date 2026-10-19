import '@testing-library/jest-dom/vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

// Expected RED failure: Component TaxonomyEditor in src/ui/src/components/TaxonomyEditor.tsx does not exist or fails import.
import * as TaxonomyEditorModule from '../components/TaxonomyEditor';

export interface TaxonomyData {
  domain: 'Work' | 'Personal';
  customer?: string;
  project?: string;
  initiative?: string;
}

export interface TaxonomyEditorProps {
  recordingId: string;
  initialTaxonomy?: TaxonomyData;
  domain?: 'Work' | 'Personal';
  customer?: string;
  project?: string;
  initiative?: string;
  onSaveAndMove?: (taxonomy: TaxonomyData) => Promise<void> | void;
  onRetag?: (taxonomy: TaxonomyData) => Promise<void> | void;
  onReTranscribe?: (options: { model: string; recordingId?: string }) => Promise<void> | void;
  onReSummarize?: (options?: { recordingId?: string }) => Promise<void> | void;
  apiUrl?: string;
  className?: string;
}

export interface ReTranscribeDialogProps {
  isOpen: boolean;
  recordingId?: string;
  onClose?: () => void;
  onConfirm?: (model: string) => Promise<void> | void;
  onReTranscribe?: (options: { model: string; recordingId?: string }) => Promise<void> | void;
}

const TaxonomyEditor = (TaxonomyEditorModule.TaxonomyEditor ||
  TaxonomyEditorModule.default) as React.ComponentType<TaxonomyEditorProps>;

const ReTranscribeDialog = (TaxonomyEditorModule.ReTranscribeDialog ||
  TaxonomyEditorModule.default) as React.ComponentType<ReTranscribeDialogProps>;

const defaultTaxonomy: TaxonomyData = {
  domain: 'Work',
  customer: 'Acme Corp',
  project: 'Project Apollo',
  initiative: 'Cloud Modernization',
};

function renderTaxonomyEditor(props: Partial<TaxonomyEditorProps> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  const mergedProps: TaxonomyEditorProps = {
    recordingId: 'rec-test-123',
    initialTaxonomy: defaultTaxonomy,
    domain: defaultTaxonomy.domain,
    customer: defaultTaxonomy.customer,
    project: defaultTaxonomy.project,
    initiative: defaultTaxonomy.initiative,
    ...props,
  };

  return render(
    <QueryClientProvider client={queryClient}>
      <TaxonomyEditor {...mergedProps} />
    </QueryClientProvider>
  );
}

describe('TaxonomyEditor', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('TaxonomyEditor_InitialRender_DisplaysAllTaxonomyFields', () => {
    renderTaxonomyEditor();

    // 1. Domain (Work / Personal)
    const domainField =
      screen.queryByLabelText(/domain/i) ||
      screen.queryByRole('combobox', { name: /domain/i }) ||
      screen.queryByTestId('taxonomy-domain');
    const workOptionOrRadio =
      screen.queryByRole('option', { name: /work/i }) ||
      screen.queryByRole('radio', { name: /work/i }) ||
      screen.queryByText(/work/i);
    const personalOptionOrRadio =
      screen.queryByRole('option', { name: /personal/i }) ||
      screen.queryByRole('radio', { name: /personal/i }) ||
      screen.queryByText(/personal/i);

    expect(domainField || (workOptionOrRadio && personalOptionOrRadio)).toBeTruthy();

    // 2. Customer
    const customerInput =
      screen.queryByLabelText(/customer/i) ||
      screen.queryByPlaceholderText(/customer/i) ||
      screen.queryByTestId('taxonomy-customer');
    expect(customerInput).toBeInTheDocument();
    expect(customerInput).toHaveValue('Acme Corp');

    // 3. Project
    const projectInput =
      screen.queryByLabelText(/project/i) ||
      screen.queryByPlaceholderText(/project/i) ||
      screen.queryByTestId('taxonomy-project');
    expect(projectInput).toBeInTheDocument();
    expect(projectInput).toHaveValue('Project Apollo');

    // 4. Initiative
    const initiativeInput =
      screen.queryByLabelText(/initiative/i) ||
      screen.queryByPlaceholderText(/initiative/i) ||
      screen.queryByTestId('taxonomy-initiative');
    expect(initiativeInput).toBeInTheDocument();
    expect(initiativeInput).toHaveValue('Cloud Modernization');
  });

  it('TaxonomyEditor_SaveAndMoveClicked_CallsRetagHandlerWithUpdatedMetadata', async () => {
    const user = userEvent.setup();
    const onSaveAndMove = vi.fn().mockResolvedValue(undefined);
    const onRetag = vi.fn().mockResolvedValue(undefined);

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      return new Response(JSON.stringify({ status: 'ok', success: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    });

    renderTaxonomyEditor({ onSaveAndMove, onRetag });

    // Update Customer field
    const customerInput =
      screen.queryByLabelText(/customer/i) ||
      screen.queryByPlaceholderText(/customer/i) ||
      screen.getByTestId('taxonomy-customer');
    await user.clear(customerInput);
    await user.type(customerInput, 'Stark Industries');

    // Update Project field
    const projectInput =
      screen.queryByLabelText(/project/i) ||
      screen.queryByPlaceholderText(/project/i) ||
      screen.getByTestId('taxonomy-project');
    await user.clear(projectInput);
    await user.type(projectInput, 'Mark VII');

    // Update Initiative field
    const initiativeInput =
      screen.queryByLabelText(/initiative/i) ||
      screen.queryByPlaceholderText(/initiative/i) ||
      screen.getByTestId('taxonomy-initiative');
    await user.clear(initiativeInput);
    await user.type(initiativeInput, 'Clean Energy');

    // Update Domain field to Personal if selector exists
    const personalRadio = screen.queryByRole('radio', { name: /personal/i });
    const domainSelect =
      screen.queryByLabelText(/domain/i) ||
      screen.queryByRole('combobox', { name: /domain/i });
    if (personalRadio) {
      await user.click(personalRadio);
    } else if (domainSelect) {
      await user.selectOptions(domainSelect, 'Personal');
    }

    // Click "Save & Move" button
    const saveAndMoveButton =
      screen.queryByRole('button', { name: /save & move|save and move/i }) ||
      screen.getByTestId('save-and-move-button');
    expect(saveAndMoveButton).toBeInTheDocument();

    await user.click(saveAndMoveButton);

    const handlerCalled =
      onSaveAndMove.mock.calls.length > 0 || onRetag.mock.calls.length > 0;
    const fetchCalled = fetchSpy.mock.calls.some(([url, options]) => {
      const urlStr = typeof url === 'string' ? url : (url as Request).url;
      return (
        (urlStr.includes('/retag') ||
          urlStr.includes('/taxonomy') ||
          urlStr.includes('/move')) &&
        (!options ||
          options.method === 'POST' ||
          options.method === 'PUT' ||
          options.method === 'PATCH')
      );
    });

    expect(handlerCalled || fetchCalled).toBe(true);

    if (onSaveAndMove.mock.calls.length > 0) {
      const payload = onSaveAndMove.mock.calls[0][0];
      expect(payload.customer).toBe('Stark Industries');
      expect(payload.project).toBe('Mark VII');
      expect(payload.initiative).toBe('Clean Energy');
    } else if (onRetag.mock.calls.length > 0) {
      const payload = onRetag.mock.calls[0][0];
      expect(payload.customer).toBe('Stark Industries');
      expect(payload.project).toBe('Mark VII');
      expect(payload.initiative).toBe('Clean Energy');
    } else if (fetchCalled) {
      const call = fetchSpy.mock.calls.find(([url]) => {
        const urlStr = typeof url === 'string' ? url : (url as Request).url;
        return (
          urlStr.includes('/retag') ||
          urlStr.includes('/taxonomy') ||
          urlStr.includes('/move')
        );
      });
      const body = JSON.parse((call?.[1]?.body as string) || '{}');
      expect(body.customer).toBe('Stark Industries');
      expect(body.project).toBe('Mark VII');
      expect(body.initiative).toBe('Clean Energy');
    }
  });

  it('TaxonomyEditor_ReTranscribeButtonClicked_OpensModalAllowingModelPickAndTriggersReTranscription', async () => {
    const user = userEvent.setup();
    const onReTranscribe = vi.fn().mockResolvedValue(undefined);

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      return new Response(JSON.stringify({ status: 'ok', jobId: 'job-123' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    });

    renderTaxonomyEditor({ onReTranscribe });

    // Click "Re-Transcribe" button
    const retranscribeBtn =
      screen.queryByRole('button', { name: /re-transcribe|retranscribe/i }) ||
      screen.getByTestId('retranscribe-button');
    expect(retranscribeBtn).toBeInTheDocument();

    await user.click(retranscribeBtn);

    // Verify modal / dialog is opened and displays model choices: whisper-large-v3-turbo and parakeet-tdt-0.6b-v2
    expect(screen.getByText(/whisper-large-v3-turbo/i)).toBeInTheDocument();
    expect(screen.getByText(/parakeet-tdt-0.6b-v2/i)).toBeInTheDocument();

    // Select parakeet-tdt-0.6b-v2 or whisper-large-v3-turbo
    const parakeetOption =
      screen.queryByRole('radio', { name: /parakeet-tdt-0.6b-v2/i }) ||
      screen.queryByRole('option', { name: /parakeet-tdt-0.6b-v2/i }) ||
      screen.queryByText(/parakeet-tdt-0.6b-v2/i);
    const modelSelect =
      screen.queryByLabelText(/model/i) ||
      screen.queryByRole('combobox', { name: /model/i }) ||
      screen.queryByTestId('model-select');

    if (parakeetOption) {
      await user.click(parakeetOption);
    } else if (modelSelect) {
      await user.selectOptions(modelSelect, 'parakeet-tdt-0.6b-v2');
    }

    // Confirm / start re-transcription
    const confirmBtn =
      screen.queryByRole('button', {
        name: /start|confirm|submit|trigger|re-transcribe/i,
      }) || screen.getByTestId('retranscribe-confirm-button');
    expect(confirmBtn).toBeInTheDocument();

    await user.click(confirmBtn);

    const callbackCalled = onReTranscribe.mock.calls.length > 0;
    const fetchCalled = fetchSpy.mock.calls.some(([url, options]) => {
      const urlStr = typeof url === 'string' ? url : (url as Request).url;
      return (
        (urlStr.includes('/retranscribe') || urlStr.includes('/transcribe')) &&
        (!options || options.method === 'POST')
      );
    });

    expect(callbackCalled || fetchCalled).toBe(true);

    if (callbackCalled) {
      const arg = onReTranscribe.mock.calls[0][0];
      const model = typeof arg === 'string' ? arg : arg.model;
      expect(['parakeet-tdt-0.6b-v2', 'whisper-large-v3-turbo']).toContain(model);
    } else if (fetchCalled) {
      const call = fetchSpy.mock.calls.find(([url]) => {
        const urlStr = typeof url === 'string' ? url : (url as Request).url;
        return (
          urlStr.includes('/retranscribe') || urlStr.includes('/transcribe')
        );
      });
      const body = JSON.parse((call?.[1]?.body as string) || '{}');
      expect(['parakeet-tdt-0.6b-v2', 'whisper-large-v3-turbo']).toContain(
        body.model
      );
    }
  });

  it('TaxonomyEditor_ReSummarizeButtonClicked_TriggersReSummarizationApiCall', async () => {
    const user = userEvent.setup();
    const onReSummarize = vi.fn().mockResolvedValue(undefined);

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      return new Response(
        JSON.stringify({ status: 'ok', jobId: 'summary-job-456' }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    });

    renderTaxonomyEditor({ onReSummarize });

    const resummarizeBtn =
      screen.queryByRole('button', { name: /re-summarize|resummarize/i }) ||
      screen.getByTestId('resummarize-button');
    expect(resummarizeBtn).toBeInTheDocument();

    await user.click(resummarizeBtn);

    const callbackCalled = onReSummarize.mock.calls.length > 0;
    const fetchCalled = fetchSpy.mock.calls.some(([url, options]) => {
      const urlStr = typeof url === 'string' ? url : (url as Request).url;
      return (
        (urlStr.includes('/resummarize') || urlStr.includes('/summarize')) &&
        (!options || options.method === 'POST')
      );
    });

    expect(callbackCalled || fetchCalled).toBe(true);
  });
});

describe('ReTranscribeDialog', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('ReTranscribeDialog_StandaloneRender_AllowsModelPickAndTriggersConfirmation', async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const onReTranscribe = vi.fn().mockResolvedValue(undefined);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <ReTranscribeDialog
          isOpen={true}
          recordingId="rec-test-123"
          onConfirm={onConfirm}
          onReTranscribe={onReTranscribe}
        />
      </QueryClientProvider>
    );

    // Both models should be selectable
    expect(screen.getByText(/whisper-large-v3-turbo/i)).toBeInTheDocument();
    expect(screen.getByText(/parakeet-tdt-0.6b-v2/i)).toBeInTheDocument();

    const whisperOption =
      screen.queryByRole('radio', { name: /whisper-large-v3-turbo/i }) ||
      screen.queryByRole('option', { name: /whisper-large-v3-turbo/i }) ||
      screen.queryByText(/whisper-large-v3-turbo/i);
    const modelSelect =
      screen.queryByLabelText(/model/i) ||
      screen.queryByRole('combobox', { name: /model/i }) ||
      screen.queryByTestId('model-select');

    if (whisperOption) {
      await user.click(whisperOption);
    } else if (modelSelect) {
      await user.selectOptions(modelSelect, 'whisper-large-v3-turbo');
    }

    const confirmBtn =
      screen.queryByRole('button', {
        name: /start|confirm|submit|trigger|re-transcribe/i,
      }) || screen.getByTestId('retranscribe-confirm-button');
    expect(confirmBtn).toBeInTheDocument();

    await user.click(confirmBtn);

    const called =
      onConfirm.mock.calls.length > 0 || onReTranscribe.mock.calls.length > 0;
    expect(called).toBe(true);

    const callArg =
      onConfirm.mock.calls[0]?.[0] || onReTranscribe.mock.calls[0]?.[0];
    const chosenModel = typeof callArg === 'string' ? callArg : callArg?.model;
    expect(chosenModel).toBe('whisper-large-v3-turbo');
  });
});
