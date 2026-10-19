import React from 'react';
import { Loader2, CheckCircle2, Clock } from 'lucide-react';

export type PipelineStage =
  | 'Downloading'
  | 'Transcribing'
  | 'Diarizing'
  | 'Classifying'
  | 'Summarizing';

export interface PipelineProgressBarProps {
  stage?: PipelineStage | string;
  status?: string;
  className?: string;
}

const STAGE_PROGRESS: Record<string, number> = {
  Downloading: 15,
  Transcribing: 35,
  Diarizing: 60,
  Classifying: 80,
  Summarizing: 95,
  Completed: 100,
};

export const PipelineProgressBar: React.FC<PipelineProgressBarProps> = ({
  stage,
  status,
  className = '',
}) => {
  if (!stage) {
    if (status === 'Completed') {
      return (
        <span
          className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-950 text-emerald-400 border border-emerald-800 ${className}`}
        >
          <CheckCircle2 className="w-3.5 h-3.5" />
          <span>Completed</span>
        </span>
      );
    }

    return (
      <span
        className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-slate-800 text-slate-300 border border-slate-700 ${className}`}
      >
        <Clock className="w-3.5 h-3.5" />
        <span>{status || 'Idle'}</span>
      </span>
    );
  }

  const percent = STAGE_PROGRESS[stage] ?? 50;

  return (
    <div className={`flex flex-col gap-1 min-w-[130px] ${className}`}>
      <div className="flex items-center justify-between text-xs">
        <span className="inline-flex items-center gap-1.5 font-medium text-amber-400">
          <Loader2 className="w-3 h-3 animate-spin text-amber-400 shrink-0" />
          <span>{stage}</span>
        </span>
        <span className="text-[10px] font-mono text-slate-400">{percent}%</span>
      </div>
      <div className="w-full bg-slate-800 rounded-full h-1.5 overflow-hidden">
        <div
          className="bg-amber-400 h-1.5 rounded-full transition-all duration-300"
          style={{ width: `${percent}%` }}
        />
      </div>
    </div>
  );
};

export default PipelineProgressBar;
