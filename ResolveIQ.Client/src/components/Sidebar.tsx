import React from 'react';
import { NavLink } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { RoleBadge } from './Badge';
import {
  LayoutDashboard,
  Ticket,
  CheckSquare,
  Users,
  BookOpen,
  AlertTriangle,
  Activity,
  Layers,
  X,
  PlusCircle,
} from 'lucide-react';

interface SidebarProps {
  isOpen: boolean;
  onClose: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ isOpen, onClose }) => {
  const { user } = useAuth();
  const role = user?.role;

  const dashboardPath =
    role === 'Admin'
      ? '/admin/dashboard'
      : role === 'Agent'
      ? '/agent/dashboard'
      : '/employee/dashboard';

  const navClass = ({ isActive }: { isActive: boolean }) =>
    `flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-all duration-150 ${
      isActive
        ? 'bg-indigo-600/20 text-indigo-400 border border-indigo-500/30 shadow-sm'
        : 'text-slate-400 hover:text-slate-100 hover:bg-slate-800/60'
    }`;

  return (
    <>
      {/* Mobile Backdrop */}
      {isOpen && (
        <div
          className="fixed inset-0 z-40 bg-slate-950/80 backdrop-blur-sm lg:hidden"
          onClick={onClose}
        />
      )}

      {/* Sidebar Container */}
      <aside
        className={`fixed top-0 bottom-0 left-0 z-50 w-64 bg-slate-900/95 border-r border-slate-800 flex flex-col transition-transform duration-300 lg:static lg:translate-x-0 ${
          isOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        {/* Brand Header */}
        <div className="h-16 px-6 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-tr from-indigo-600 to-indigo-400 flex items-center justify-center shadow-md shadow-indigo-500/20">
              <Layers className="w-5 h-5 text-white" />
            </div>
            <div>
              <span className="font-bold text-lg tracking-tight bg-gradient-to-r from-white to-slate-300 bg-clip-text text-transparent">
                Resolve<span className="text-indigo-400">IQ</span>
              </span>
              <span className="block text-[10px] text-slate-400 font-mono tracking-wider -mt-1 uppercase">
                Enterprise ITSM
              </span>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-slate-200 lg:hidden"
            aria-label="Close menu"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* User Card */}
        {user && (
          <div className="p-4 mx-3 my-3 rounded-xl bg-slate-800/40 border border-slate-800">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-full bg-slate-700/80 border border-slate-600 flex items-center justify-center font-bold text-slate-200 text-sm">
                {user.fullName.charAt(0).toUpperCase()}
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-semibold text-slate-200 truncate">{user.fullName}</p>
                <div className="mt-0.5">
                  <RoleBadge role={user.role} />
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Navigation Links */}
        <nav className="flex-1 px-3 py-2 space-y-1 overflow-y-auto">
          <div className="px-3 py-1.5 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
            Overview
          </div>
          <NavLink to={dashboardPath} className={navClass} onClick={onClose}>
            <LayoutDashboard className="w-4 h-4" />
            Dashboard
          </NavLink>

          {/* Role-specific Nav Items */}
          <div className="pt-4 px-3 py-1.5 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
            {role === 'Admin' ? 'Operations' : role === 'Agent' ? 'Assigned Queue' : 'My Support'}
          </div>

          {role === 'Employee' ? (
            <>
              <NavLink to="/employee/tickets" className={navClass} onClick={onClose} end>
                <Ticket className="w-4 h-4" />
                My Tickets
              </NavLink>
              <NavLink to="/employee/tickets/new" className={navClass} onClick={onClose}>
                <PlusCircle className="w-4 h-4" />
                Submit Ticket
              </NavLink>
            </>
          ) : (
            <NavLink to="/tickets" className={navClass} onClick={onClose} end>
              <Ticket className="w-4 h-4" />
              All Tickets
            </NavLink>
          )}

          {role === 'Agent' && (
            <NavLink to="/agent/assigned" className={navClass} onClick={onClose}>
              <CheckSquare className="w-4 h-4" />
              Assigned to Me
            </NavLink>
          )}

          {role === 'Admin' && (
            <>
              <NavLink to="/admin/users" className={navClass} onClick={onClose}>
                <Users className="w-4 h-4" />
                User Management
              </NavLink>
              <NavLink to="/admin/health" className={navClass} onClick={onClose}>
                <Activity className="w-4 h-4" />
                System Health
              </NavLink>
            </>
          )}

          {(role === 'Agent' || role === 'Admin') && (
            <NavLink to="/tickets/escalations" className={navClass} onClick={onClose}>
              <AlertTriangle className="w-4 h-4" />
              SLA Escalations
            </NavLink>
          )}

          <div className="pt-4 px-3 py-1.5 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
            Resources
          </div>
          <NavLink to="/knowledge-base" className={navClass} onClick={onClose}>
            <BookOpen className="w-4 h-4" />
            Knowledge Base
          </NavLink>
        </nav>

        {/* Footer info */}
        <div className="p-4 border-t border-slate-800/80 text-xs text-slate-400 text-center">
          ResolveIQ v1.0 • .NET 10 ITSM
        </div>
      </aside>
    </>
  );
};
