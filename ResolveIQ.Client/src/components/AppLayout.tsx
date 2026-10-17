import React, { useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { Header } from './Header';

export const AppLayout: React.FC = () => {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const location = useLocation();

  // Compute title dynamically based on path
  const getPageInfo = (path: string): { title: string; subtitle?: string } => {
    if (path.includes('/admin/dashboard')) return { title: 'Operational Analytics', subtitle: 'Global ITSM metrics & health' };
    if (path.includes('/agent/dashboard')) return { title: 'Agent Workspace', subtitle: 'Assigned queues & active tickets' };
    if (path.includes('/employee/dashboard')) return { title: 'Support Portal', subtitle: 'Your active incidents & requests' };
    if (path.includes('/agent/assigned')) return { title: 'Assigned Tickets', subtitle: 'Tickets assigned to your queue' };
    if (path.includes('/admin/users')) return { title: 'User Management', subtitle: 'System users & role governance' };
    if (path.includes('/admin/health')) return { title: 'System Health', subtitle: 'ResolveIQ services & database status' };
    if (path.includes('/tickets/escalations')) return { title: 'SLA Escalations', subtitle: 'Tickets exceeding response or resolution targets' };
    if (path.includes('/tickets')) return { title: 'Ticket Management', subtitle: 'Browse, filter, and track IT support tickets' };
    if (path.includes('/knowledge-base')) return { title: 'Knowledge Base', subtitle: 'IT troubleshooting articles & documentation' };
    return { title: 'ResolveIQ' };
  };

  const { title, subtitle } = getPageInfo(location.pathname);

  return (
    <div className="flex h-screen overflow-hidden bg-slate-950">
      {/* Sidebar */}
      <Sidebar isOpen={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      {/* Main Content Area */}
      <div className="flex flex-col flex-1 min-w-0 overflow-hidden">
        <Header
          onToggleSidebar={() => setSidebarOpen((prev) => !prev)}
          title={title}
          subtitle={subtitle}
        />

        <main className="flex-1 overflow-y-auto p-4 sm:p-6 lg:p-8">
          <Outlet />
        </main>
      </div>
    </div>
  );
};
