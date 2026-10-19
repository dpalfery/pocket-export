import React, { useState, useMemo } from 'react';
import { FilterBar, DomainFacet } from './FilterBar';
import { RecordingDetailPane } from './RecordingDetailPane';
import { PipelineProgressBar } from './PipelineProgressBar';
import { Mic, CheckCircle2, ChevronRight } from 'lucide-react';

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
  summary?: string;
}

export interface RecordingsDashboardProps {
  recordings?: RecordingItem[];
  onSelectRecording?: (recording: RecordingItem) => void;
  className?: string;
}

export const RecordingsDashboard: React.FC<RecordingsDashboardProps> = ({
  recordings = [],
  onSelectRecording,
  className = '',
}) => {
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [domainFilter, setDomainFilter] = useState<DomainFacet>('All');
  const [selectedRecording, setSelectedRecording] = useState<RecordingItem | null>(null);

  const filteredRecordings = useMemo(() => {
    return recordings.filter((item) => {
      // Domain facet filter
      if (domainFilter !== 'All' && item.domain !== domainFilter) {
        return false;
      }

      // Search query filter (matches title or text content in transcript/customer/project/speakers)
      const term = searchQuery.trim().toLowerCase();
      if (term) {
        const matchesTitle = item.title.toLowerCase().includes(term);
        const matchesTranscript = item.transcript?.toLowerCase().includes(term) ?? false;
        const matchesCustomer = item.customer?.toLowerCase().includes(term) ?? false;
        const matchesProject = item.project?.toLowerCase().includes(term) ?? false;
        const matchesSpeakers = item.speakers.some((s) => s.toLowerCase().includes(term));
        const matchesRouted = item.routedPath.toLowerCase().includes(term);

        if (
          !matchesTitle &&
          !matchesTranscript &&
          !matchesCustomer &&
          !matchesProject &&
          !matchesSpeakers &&
          !matchesRouted
        ) {
          return false;
        }
      }

      return true;
    });
  }, [recordings, domainFilter, searchQuery]);

  const handleRowClick = (item: RecordingItem) => {
    setSelectedRecording(item);
    if (onSelectRecording) {
      onSelectRecording(item);
    }
  };

  return (
    <div className={`space-y-6 ${className}`}>
      {/* Search and Facet Bar */}
      <FilterBar
        searchQuery={searchQuery}
        onSearchChange={setSearchQuery}
        domainFilter={domainFilter}
        onDomainChange={setDomainFilter}
        totalCount={recordings.length}
        filteredCount={filteredRecordings.length}
      />

      {/* Main Content: Table + Optional Detail Pane */}
      <div className="flex flex-col xl:flex-row items-start gap-6">
        <div
          className={`w-full overflow-hidden transition-all duration-300 ${
            selectedRecording ? 'xl:w-7/12' : 'xl:w-full'
          }`}
        >
          <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-lg">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="border-b border-slate-800 bg-slate-950/70 text-slate-400 text-xs font-semibold uppercase tracking-wider select-none">
                    <th scope="col" className="px-4 py-3.5">
                      Date
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Title
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Duration
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Domain
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Customer
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Project
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Speakers
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Status
                    </th>
                    <th scope="col" className="px-4 py-3.5">
                      Routed Path
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/80">
                  {filteredRecordings.length === 0 ? (
                    <tr>
                      <td colSpan={9} className="px-6 py-12 text-center text-slate-500">
                        <div className="flex flex-col items-center justify-center gap-2">
                          <Mic className="w-8 h-8 text-slate-600" />
                          <p className="text-sm font-medium">No voice recordings found</p>
                          <p className="text-xs text-slate-600">
                            Adjust filters or search criteria to inspect vault recordings.
                          </p>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    filteredRecordings.map((recording) => {
                      const isSelected = selectedRecording?.id === recording.id;
                      return (
                        <tr
                          key={recording.id}
                          onClick={() => handleRowClick(recording)}
                          className={`cursor-pointer transition-colors duration-150 ${
                            isSelected
                              ? 'bg-indigo-950/40 hover:bg-indigo-950/60'
                              : 'hover:bg-slate-800/50 bg-slate-900/40'
                          }`}
                        >
                          <td className="px-4 py-3.5 text-xs text-slate-300 font-mono whitespace-nowrap">
                            {recording.date}
                          </td>
                          <td className="px-4 py-3.5 text-sm font-medium text-slate-100 whitespace-nowrap">
                            <div className="flex items-center gap-2">
                              <span>{recording.title}</span>
                              {isSelected && <ChevronRight className="w-4 h-4 text-indigo-400 shrink-0" />}
                            </div>
                          </td>
                          <td className="px-4 py-3.5 text-xs text-slate-400 font-mono whitespace-nowrap">
                            {recording.duration}
                          </td>
                          <td className="px-4 py-3.5 text-xs whitespace-nowrap">
                            <span
                              className={`inline-flex px-2 py-0.5 rounded text-xs font-semibold ${
                                recording.domain === 'Work'
                                  ? 'bg-blue-950 text-blue-300 border border-blue-800'
                                  : 'bg-emerald-950 text-emerald-300 border border-emerald-800'
                              }`}
                            >
                              {recording.domain}
                            </span>
                          </td>
                          <td className="px-4 py-3.5 text-xs text-slate-300 whitespace-nowrap">
                            {recording.customer || '—'}
                          </td>
                          <td className="px-4 py-3.5 text-xs text-slate-300 whitespace-nowrap">
                            {recording.project || '—'}
                          </td>
                          <td className="px-4 py-3.5 text-xs text-slate-300 whitespace-nowrap">
                            {recording.speakers.length > 0
                              ? recording.speakers.join(', ')
                              : '—'}
                          </td>
                          <td className="px-4 py-3.5 text-xs whitespace-nowrap">
                            {recording.activeStage ? (
                              <PipelineProgressBar
                                stage={recording.activeStage}
                                status={recording.status}
                              />
                            ) : (
                              <span
                                className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                                  recording.status === 'Completed'
                                    ? 'bg-emerald-950 text-emerald-400 border border-emerald-800'
                                    : 'bg-slate-800 text-slate-300 border border-slate-700'
                                }`}
                              >
                                {recording.status === 'Completed' && (
                                  <CheckCircle2 className="w-3.5 h-3.5" />
                                )}
                                <span>{recording.status}</span>
                              </span>
                            )}
                          </td>
                          <td className="px-4 py-3.5 text-xs font-mono text-slate-400 max-w-xs truncate">
                            <span title={recording.routedPath}>{recording.routedPath}</span>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>

        {/* Selected Recording Detail Pane */}
        {selectedRecording && (
          <div className="w-full xl:w-5/12 shrink-0">
            <RecordingDetailPane
              recording={selectedRecording}
              onClose={() => setSelectedRecording(null)}
            />
          </div>
        )}
      </div>
    </div>
  );
};

export default RecordingsDashboard;
