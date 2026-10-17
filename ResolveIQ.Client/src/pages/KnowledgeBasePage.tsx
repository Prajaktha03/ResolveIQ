import React, { useState, useEffect, useCallback } from 'react';
import { request } from '../services/api';
import { BookOpen, Search, RefreshCw, AlertCircle, Tag, Inbox } from 'lucide-react';

interface KnowledgeArticle {
  id: number;
  title: string;
  content: string;
  category: string;
  tags?: string;
  isPublished: boolean;
  createdAt: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const KnowledgeBasePage: React.FC = () => {
  const [articles, setArticles] = useState<KnowledgeArticle[]>([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchArticles = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const query = new URLSearchParams();
      if (searchTerm) query.set('search', searchTerm);
      if (categoryFilter) query.set('category', categoryFilter);
      query.set('page', '1');
      query.set('pageSize', '20');

      const data = await request<PagedResult<KnowledgeArticle>>(`/api/KnowledgeBase?${query.toString()}`);
      setArticles(data.items);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to fetch knowledge base articles.';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [searchTerm, categoryFilter]);

  useEffect(() => {
    fetchArticles();
  }, [fetchArticles]);

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 bg-slate-900 p-6 rounded-2xl border border-slate-800">
        <div>
          <div className="flex items-center gap-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
              <BookOpen className="w-3.5 h-3.5" />
              IT Knowledge Base
            </span>
          </div>
          <h2 className="text-xl font-bold text-white mt-1">Knowledge Repository</h2>
          <p className="text-sm text-slate-400">
            Self-service diagnostic guides, common troubleshooting steps, and enterprise IT documentation.
          </p>
        </div>
        <button
          onClick={fetchArticles}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition self-start sm:self-auto"
        >
          <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          <span>Refresh Articles</span>
        </button>
      </div>

      {/* Search & Filter */}
      <div className="flex flex-col sm:flex-row gap-3">
        <div className="relative flex-1">
          <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400" />
          <input
            type="text"
            placeholder="Search articles by title, keywords, or issue..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>
        <select
          value={categoryFilter}
          onChange={(e) => setCategoryFilter(e.target.value)}
          className="px-3.5 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        >
          <option value="">All Categories</option>
          <option value="Hardware">Hardware</option>
          <option value="Software">Software</option>
          <option value="Network">Network</option>
          <option value="Access & Identity">Access & Identity</option>
          <option value="Infrastructure">Infrastructure</option>
          <option value="General">General</option>
        </select>
      </div>

      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm flex items-center gap-3">
          <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      {/* Articles Grid */}
      {isLoading ? (
        <div className="py-20 flex flex-col items-center justify-center gap-3 text-slate-400">
          <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
          <p className="text-sm">Searching knowledge articles...</p>
        </div>
      ) : articles.length === 0 ? (
        <div className="bg-slate-900 p-12 rounded-2xl border border-slate-800 text-center flex flex-col items-center justify-center">
          <Inbox className="w-12 h-12 text-slate-600 mb-3" />
          <h4 className="text-base font-semibold text-slate-200">No articles found</h4>
          <p className="text-sm text-slate-400 max-w-sm mt-1">
            No published knowledge articles matched your search query.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {articles.map((article) => (
            <div key={article.id} className="bg-slate-900 p-6 rounded-2xl border border-slate-800 flex flex-col justify-between">
              <div>
                <div className="flex items-center justify-between mb-2">
                  <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
                    {article.category}
                  </span>
                  <span className="text-xs text-slate-400 font-mono">
                    {new Date(article.createdAt).toLocaleDateString()}
                  </span>
                </div>
                <h3 className="text-base font-semibold text-white mt-2">{article.title}</h3>
                <p className="text-sm text-slate-300 mt-2 line-clamp-3 leading-relaxed whitespace-pre-line">
                  {article.content}
                </p>
              </div>

              {article.tags && (
                <div className="mt-4 pt-4 border-t border-slate-800 flex flex-wrap gap-1.5 items-center">
                  <Tag className="w-3 h-3 text-slate-400" />
                  {article.tags.split(',').map((tag) => (
                    <span key={tag} className="text-[11px] px-2 py-0.5 rounded bg-slate-800 text-slate-400 font-mono">
                      {tag.trim()}
                    </span>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
