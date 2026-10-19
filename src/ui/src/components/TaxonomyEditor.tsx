import React, { useState, useEffect } from 'react';
import { Tag, Save, Sparkles, RefreshCw } from 'lucide-react';
import { ReTranscribeDialog, ReTranscribeDialogProps } from './ReTranscribeDialog';

export { ReTranscribeDialog };
export type { ReTranscribeDialogProps };

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
  onReTranscribe?: (options: { model: string; recordingId?: string; beamSize?: number }) => Promise<void> | void;
  onReSummarize?: (options?: { recordingId?: string }) => Promise<void> | void;
  apiUrl?: string;
  className?: string;
}

export const TaxonomyEditor: React.FC<TaxonomyEditorProps> = ({
  recordingId,
  initialTaxonomy,
  domain: initialDomainProp,
  customer: initialCustomerProp,
  project: initialProjectProp,
  initiative: initialInitiativeProp,
  onSaveAndMove,
  onRetag,
  onReTranscribe,
  onReSummarize,
  apiUrl,
  className = '',
}) => {
  const [domain, setDomain] = useState<'Work' | 'Personal'>(() => {
    return initialDomainProp ?? initialTaxonomy?.domain ?? 'Work';
  });
  const [customer, setCustomer] = useState<string>(() => {
    return initialCustomerProp ?? initialTaxonomy?.customer ?? '';
  });
  const [project, setProject] = useState<string>(() => {
    return initialProjectProp ?? initialTaxonomy?.project ?? '';
  });
  const [initiative, setInitiative] = useState<string>(() => {
    return initialInitiativeProp ?? initialTaxonomy?.initiative ?? '';
  });

  const [isSaving, setIsSaving] = useState(false);
  const [isSummarizing, setIsSummarizing] = useState(false);
  const [isReTranscribeOpen, setIsReTranscribeOpen] = useState(false);
  const [statusMessage, setStatusMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    if (initialDomainProp !== undefined) {
      setDomain(initialDomainProp);
    } else if (initialTaxonomy?.domain !== undefined) {
      setDomain(initialTaxonomy.domain);
    }
  }, [initialDomainProp, initialTaxonomy?.domain]);

  useEffect(() => {
    if (initialCustomerProp !== undefined) {
      setCustomer(initialCustomerProp);
    } else if (initialTaxonomy?.customer !== undefined) {
      setCustomer(initialTaxonomy.customer);
    }
  }, [initialCustomerProp, initialTaxonomy?.customer]);

  useEffect(() => {
    if (initialProjectProp !== undefined) {
      setProject(initialProjectProp);
    } else if (initialTaxonomy?.project !== undefined) {
      setProject(initialTaxonomy.project);
    }
  }, [initialProjectProp, initialTaxonomy?.project]);

  useEffect(() => {
    if (initialInitiativeProp !== undefined) {
      setInitiative(initialInitiativeProp);
    } else if (initialTaxonomy?.initiative !== undefined) {
      setInitiative(initialTaxonomy.initiative);
    }
  }, [initialInitiativeProp, initialTaxonomy?.initiative]);

  const handleSaveAndMove = async () => {
    setIsSaving(true);
    setStatusMessage(null);
    const payload: TaxonomyData = {
      domain,
      customer,
      project,
      initiative,
    };

    try {
      if (onSaveAndMove) {
        await onSaveAndMove(payload);
      }
      if (onRetag) {
        await onRetag(payload);
      }
      if (!onSaveAndMove && !onRetag) {
        const base = apiUrl ? apiUrl.replace(/\/$/, '') : '';
        const res = await fetch(`${base}/api/recordings/${recordingId}/retag`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        });
        if (!res.ok) {
          throw new Error(`Failed to retag recording: ${res.statusText}`);
        }
      }
      setStatusMessage({ type: 'success', text: 'Taxonomy saved and routed successfully.' });
    } catch (err) {
      setStatusMessage({
        type: 'error',
        text: err instanceof Error ? err.message : 'Failed to save taxonomy',
      });
    } finally {
      setIsSaving(false);
    }
  };

  const handleReSummarize = async () => {
    setIsSummarizing(true);
    setStatusMessage(null);
    try {
      if (onReSummarize) {
        await onReSummarize({ recordingId });
      } else {
        const base = apiUrl ? apiUrl.replace(/\/$/, '') : '';
        const res = await fetch(`${base}/api/recordings/${recordingId}/resummarize`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ recordingId }),
        });
        if (!res.ok) {
          throw new Error(`Failed to resummarize recording: ${res.statusText}`);
        }
      }
      setStatusMessage({ type: 'success', text: 'Summarization job queued.' });
    } catch (err) {
      setStatusMessage({
        type: 'error',
        text: err instanceof Error ? err.message : 'Failed to trigger summarization',
      });
    } finally {
      setIsSummarizing(false);
    }
  };

  return (
    <div className={`bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-xl text-slate-100 flex flex-col gap-5 ${className}`}>
      <div
        aria-hidden={isReTranscribeOpen ? true : undefined}
        className="flex flex-col gap-5"
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-slate-800 pb-3">
          <div className="flex items-center gap-2">
            <Tag className="w-5 h-5 text-indigo-400" />
            <h2 className="text-base font-semibold text-white tracking-tight">
              Taxonomy & Routing
            </h2>
          </div>
          <span className="text-xs font-mono text-slate-400 bg-slate-950 px-2 py-1 rounded border border-slate-800">
            {recordingId}
          </span>
        </div>

        {statusMessage && (
          <div
            className={`p-3 rounded-lg text-xs border ${
              statusMessage.type === 'success'
                ? 'bg-emerald-950/40 border-emerald-800/60 text-emerald-300'
                : 'bg-rose-950/40 border-rose-800/60 text-rose-300'
            }`}
          >
            {statusMessage.text}
          </div>
        )}

        {/* Fields */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          {/* Domain */}
          <div>
            <label
              htmlFor="taxonomy-domain"
              className="block text-xs font-semibold text-slate-400 uppercase tracking-wider mb-1.5"
            >
              Domain
            </label>
            <select
              id="taxonomy-domain"
              name="domain"
              aria-label="Domain"
              data-testid="taxonomy-domain"
              value={domain}
              onChange={(e) => setDomain(e.target.value as 'Work' | 'Personal')}
              className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            >
              <option value="Work">Work</option>
              <option value="Personal">Personal</option>
            </select>
          </div>

          {/* Customer */}
          <div>
            <label
              htmlFor="taxonomy-customer"
              className="block text-xs font-semibold text-slate-400 uppercase tracking-wider mb-1.5"
            >
              Customer
            </label>
            <input
              id="taxonomy-customer"
              name="customer"
              type="text"
              aria-label="Customer"
              placeholder="Customer"
              data-testid="taxonomy-customer"
              value={customer}
              onChange={(e) => setCustomer(e.target.value)}
              className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-slate-100 placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>

          {/* Project */}
          <div>
            <label
              htmlFor="taxonomy-project"
              className="block text-xs font-semibold text-slate-400 uppercase tracking-wider mb-1.5"
            >
              Project
            </label>
            <input
              id="taxonomy-project"
              name="project"
              type="text"
              aria-label="Project"
              placeholder="Project"
              data-testid="taxonomy-project"
              value={project}
              onChange={(e) => setProject(e.target.value)}
              className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-slate-100 placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>

          {/* Initiative */}
          <div>
            <label
              htmlFor="taxonomy-initiative"
              className="block text-xs font-semibold text-slate-400 uppercase tracking-wider mb-1.5"
            >
              Initiative
            </label>
            <input
              id="taxonomy-initiative"
              name="initiative"
              type="text"
              aria-label="Initiative"
              placeholder="Initiative"
              data-testid="taxonomy-initiative"
              value={initiative}
              onChange={(e) => setInitiative(e.target.value)}
              className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-slate-100 placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
        </div>

        {/* Actions */}
        <div className="flex flex-wrap items-center justify-between gap-3 pt-3 border-t border-slate-800">
          <div className="flex items-center gap-2">
            <button
              type="button"
              data-testid="retranscribe-button"
              onClick={() => setIsReTranscribeOpen(true)}
              className="px-3 py-2 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-sm font-medium transition flex items-center gap-1.5"
            >
              <RefreshCw className="w-4 h-4 text-slate-400" />
              Re-Transcribe
            </button>
            <button
              type="button"
              data-testid="resummarize-button"
              onClick={handleReSummarize}
              disabled={isSummarizing}
              className="px-3 py-2 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-sm font-medium transition flex items-center gap-1.5 disabled:opacity-50"
            >
              <Sparkles className="w-4 h-4 text-indigo-400" />
              Re-Summarize
            </button>
          </div>

          <button
            type="button"
            data-testid="save-and-move-button"
            onClick={handleSaveAndMove}
            disabled={isSaving}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg text-sm font-medium transition flex items-center gap-1.5 disabled:opacity-50 shadow-md"
          >
            <Save className="w-4 h-4" />
            Save & Move
          </button>
        </div>
      </div>

      {/* Re-Transcribe Dialog Modal */}
      <ReTranscribeDialog
        isOpen={isReTranscribeOpen}
        recordingId={recordingId}
        onClose={() => setIsReTranscribeOpen(false)}
        onReTranscribe={onReTranscribe}
        apiUrl={apiUrl}
      />
    </div>
  );
};

export default TaxonomyEditor;
