import React from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { getRoleHomePath } from '../components/ProtectedRoute';
import { ShieldAlert, ArrowLeft } from 'lucide-react';

export const ForbiddenPage: React.FC = () => {
  const { user } = useAuth();
  const homePath = getRoleHomePath(user?.role);

  return (
    <div className="min-h-[70vh] flex flex-col items-center justify-center text-center px-4">
      <div className="w-16 h-16 rounded-2xl bg-rose-500/10 border border-rose-500/20 flex items-center justify-center mb-6">
        <ShieldAlert className="w-8 h-8 text-rose-400" />
      </div>
      <h1 className="text-2xl font-bold text-white tracking-tight mb-2">Access Denied (403)</h1>
      <p className="text-sm text-slate-400 max-w-md mb-8">
        Your current role (<span className="text-indigo-400 font-semibold">{user?.role || 'Guest'}</span>) is not authorized to access this resource. Backend security rules remain enforced.
      </p>
      <Link
        to={homePath}
        className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 transition-all shadow-md shadow-indigo-600/20"
      >
        <ArrowLeft className="w-4 h-4" />
        Return to My Workspace
      </Link>
    </div>
  );
};

export const NotFoundPage: React.FC = () => {
  const { user } = useAuth();
  const homePath = getRoleHomePath(user?.role);

  return (
    <div className="min-h-[70vh] flex flex-col items-center justify-center text-center px-4">
      <div className="w-16 h-16 rounded-2xl bg-slate-800 border border-slate-700 flex items-center justify-center mb-6">
        <span className="text-2xl font-mono font-bold text-slate-400">404</span>
      </div>
      <h1 className="text-2xl font-bold text-white tracking-tight mb-2">Page Not Found</h1>
      <p className="text-sm text-slate-400 max-w-md mb-8">
        The requested URL was not found on this server.
      </p>
      <Link
        to={homePath}
        className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 transition-all shadow-md shadow-indigo-600/20"
      >
        <ArrowLeft className="w-4 h-4" />
        Return to Dashboard
      </Link>
    </div>
  );
};
