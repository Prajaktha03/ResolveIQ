import React, { useState, useEffect, useCallback } from 'react';
import { healthApi } from '../services/api';
import { Activity, Server, Database, RefreshCw, CheckCircle2, AlertTriangle } from 'lucide-react';

export const SystemHealthPage: React.FC = () => {
  const [healthData, setHealthData] = useState<{ status: string; database: string } | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [lastChecked, setLastChecked] = useState<Date | null>(null);

  const checkHealth = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await healthApi.checkReady();
      setHealthData(data);
    } catch {
      setHealthData({ status: 'Unhealthy', database: 'Unreachable' });
    } finally {
      setIsLoading(false);
      setLastChecked(new Date());
    }
  }, []);

  useEffect(() => {
    checkHealth();
  }, [checkHealth]);

  const isHealthy = healthData?.status === 'Healthy';

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 bg-slate-900 p-6 rounded-2xl border border-slate-800">
        <div>
          <div className="flex items-center gap-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
              <Activity className="w-3.5 h-3.5" />
              Infrastructure Monitoring
            </span>
          </div>
          <h2 className="text-xl font-bold text-white mt-1">System Health & Probes</h2>
          <p className="text-sm text-slate-400">
            Real-time status of backend services, PostgreSQL database connectivity, and readiness probes.
          </p>
        </div>
        <button
          onClick={checkHealth}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition self-start sm:self-auto"
        >
          <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          <span>Probe Now</span>
        </button>
      </div>

      {/* Main Status Display */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800">
          <div className="flex items-center justify-between mb-4">
            <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">Overall API Status</span>
            {isHealthy ? (
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                <CheckCircle2 className="w-3.5 h-3.5" />
                Healthy
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/20">
                <AlertTriangle className="w-3.5 h-3.5" />
                Degraded / Unreachable
              </span>
            )}
          </div>
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-xl bg-indigo-500/10 border border-indigo-500/20 flex items-center justify-center text-indigo-400">
              <Server className="w-6 h-6" />
            </div>
            <div>
              <div className="text-base font-semibold text-white">ResolveIQ Backend API</div>
              <div className="text-xs text-slate-400">ASP.NET Core .NET 10 Web API</div>
            </div>
          </div>
          <div className="mt-6 pt-4 border-t border-slate-800 text-xs text-slate-400 flex items-center justify-between">
            <span>Readiness probe:</span>
            <code className="text-slate-300 font-mono">/health/ready</code>
          </div>
        </div>

        <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800">
          <div className="flex items-center justify-between mb-4">
            <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">Database Status</span>
            {healthData?.database === 'Healthy' || healthData?.database === 'Operational' ? (
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                <CheckCircle2 className="w-3.5 h-3.5" />
                Operational
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                <span className="w-1.5 h-1.5 rounded-full bg-amber-400"></span>
                {healthData?.database || 'Checking...'}
              </span>
            )}
          </div>
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-xl bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
              <Database className="w-6 h-6" />
            </div>
            <div>
              <div className="text-base font-semibold text-white">PostgreSQL Primary Cluster</div>
              <div className="text-xs text-slate-400">Npgsql Entity Framework Core 10</div>
            </div>
          </div>
          <div className="mt-6 pt-4 border-t border-slate-800 text-xs text-slate-400 flex items-center justify-between">
            <span>Last checked:</span>
            <span className="text-slate-300 font-mono">{lastChecked ? lastChecked.toLocaleTimeString() : '—'}</span>
          </div>
        </div>
      </div>
    </div>
  );
};
