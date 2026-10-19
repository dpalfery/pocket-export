import React from 'react';
import { Search, Filter, X } from 'lucide-react';

export type DomainFacet = 'All' | 'Work' | 'Personal';

export interface FilterBarProps {
  searchQuery: string;
  onSearchChange: (query: string) => void;
  domainFilter: DomainFacet;
  onDomainChange: (domain: DomainFacet) => void;
  totalCount?: number;
  filteredCount?: number;
  className?: string;
}

export const FilterBar: React.FC<FilterBarProps> = ({
  searchQuery,
  onSearchChange,
  domainFilter,
  onDomainChange,
  totalCount,
  filteredCount,
  className = '',
}) => {
  return (
    <div
      className={`flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3 p-4 bg-slate-900 border border-slate-800 rounded-xl shadow-sm ${className}`}
    >
      <div className="relative flex-1">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 pointer-events-none" />
        <input
          type="search"
          role="searchbox"
          aria-label="Search recordings"
          placeholder="Search recordings by title, transcript, project, speakers..."
          value={searchQuery}
          onChange={(e) => onSearchChange(e.target.value)}
          className="w-full pl-9 pr-9 py-2 bg-slate-950 border border-slate-800 rounded-lg text-sm text-slate-100 placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent transition"
        />
        {searchQuery && (
          <button
            type="button"
            aria-label="Clear search"
            onClick={() => onSearchChange('')}
            className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-200 p-0.5 rounded transition"
          >
            <X className="w-4 h-4" />
          </button>
        )}
      </div>

      <div className="flex items-center gap-3 self-end sm:self-auto">
        <div className="flex items-center gap-2">
          <Filter className="w-4 h-4 text-slate-400 hidden sm:inline-block" />
          <label
            htmlFor="domain-filter"
            className="text-xs font-medium text-slate-400 whitespace-nowrap"
          >
            Domain:
          </label>
          <select
            id="domain-filter"
            data-testid="domain-filter"
            role="combobox"
            aria-label="Domain"
            value={domainFilter}
            onChange={(e) => onDomainChange(e.target.value as DomainFacet)}
            className="bg-slate-950 border border-slate-800 text-slate-200 text-sm rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500 cursor-pointer font-medium"
          >
            <option value="All">All</option>
            <option value="Work">Work</option>
            <option value="Personal">Personal</option>
          </select>
        </div>

        {totalCount !== undefined && filteredCount !== undefined && (
          <span className="text-xs text-slate-400 font-mono whitespace-nowrap pl-2 border-l border-slate-800">
            {filteredCount} of {totalCount}
          </span>
        )}
      </div>
    </div>
  );
};

export default FilterBar;
