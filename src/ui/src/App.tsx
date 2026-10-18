import { useState } from 'react';
import { FolderSync, Mic, RefreshCw, CheckCircle2 } from 'lucide-react';

export default function App() {
  const [syncStatus] = useState<string>('Ready');

  return (
    <div className="min-h-screen flex flex-col items-center justify-center p-8 bg-slate-950 text-slate-100">
      <header className="text-center mb-8">
        <div className="flex items-center justify-center gap-3 mb-2">
          <Mic className="w-8 h-8 text-indigo-400" />
          <h1 className="text-3xl font-bold tracking-tight">Pocket Export</h1>
        </div>
        <p className="text-slate-400">Desktop Pipeline & Vault Synchronizer</p>
      </header>

      <main className="w-full max-w-md bg-slate-900 border border-slate-800 rounded-xl p-6 shadow-xl">
        <div className="flex items-center justify-between p-4 bg-slate-800/50 rounded-lg mb-4">
          <div className="flex items-center gap-3">
            <FolderSync className="w-5 h-5 text-indigo-400" />
            <span className="text-sm font-medium">Vault Sync Status</span>
          </div>
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-950 text-emerald-400 border border-emerald-800">
            <CheckCircle2 className="w-3.5 h-3.5" />
            {syncStatus}
          </span>
        </div>

        <button
          type="button"
          className="w-full py-2.5 px-4 bg-indigo-600 hover:bg-indigo-500 transition-colors rounded-lg font-medium text-sm flex items-center justify-center gap-2 text-white shadow-lg shadow-indigo-600/20"
        >
          <RefreshCw className="w-4 h-4" />
          Sync Vault Now
        </button>
      </main>
    </div>
  );
}
