import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ProtectedRoute, getRoleHomePath } from './components/ProtectedRoute';
import { AppLayout } from './components/AppLayout';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { EmployeeDashboard } from './pages/EmployeeDashboard';
import { CreateTicketPage } from './pages/CreateTicketPage';
import { MyTicketsPage } from './pages/MyTicketsPage';
import { TicketDetailPage } from './pages/TicketDetailPage';
import { AgentDashboard } from './pages/AgentDashboard';
import { AssignedTicketsPage } from './pages/AssignedTicketsPage';
import { AdminDashboard } from './pages/AdminDashboard';
import { TicketsPage } from './pages/TicketsPage';
import { UserManagementPage } from './pages/UserManagementPage';
import { SystemHealthPage } from './pages/SystemHealthPage';
import { KnowledgeBasePage } from './pages/KnowledgeBasePage';
import { ForbiddenPage, NotFoundPage } from './pages/ForbiddenPage';

const RootRedirect: React.FC = () => {
  const { user, isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div className="min-h-screen bg-slate-950 flex items-center justify-center">
        <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
      </div>
    );
  }

  if (!isAuthenticated || !user) {
    return <Navigate to="/login" replace />;
  }

  return <Navigate to={getRoleHomePath(user.role)} replace />;
};

export const App: React.FC = () => {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          {/* Public Routes */}
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />

          {/* Root Redirect */}
          <Route path="/" element={<RootRedirect />} />

          {/* Protected Application Workspace */}
          <Route
            element={
              <ProtectedRoute>
                <AppLayout />
              </ProtectedRoute>
            }
          >
            {/* Employee Space */}
            <Route
              path="/employee/dashboard"
              element={
                <ProtectedRoute allowedRoles={['Employee', 'Agent', 'Admin']}>
                  <EmployeeDashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/employee/tickets"
              element={
                <ProtectedRoute allowedRoles={['Employee', 'Agent', 'Admin']}>
                  <MyTicketsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/employee/tickets/new"
              element={
                <ProtectedRoute allowedRoles={['Employee', 'Agent', 'Admin']}>
                  <CreateTicketPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/employee/tickets/:id"
              element={
                <ProtectedRoute allowedRoles={['Employee', 'Agent', 'Admin']}>
                  <TicketDetailPage />
                </ProtectedRoute>
              }
            />

            {/* Agent Space */}
            <Route
              path="/agent/dashboard"
              element={
                <ProtectedRoute allowedRoles={['Agent', 'Admin']}>
                  <AgentDashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/agent/assigned"
              element={
                <ProtectedRoute allowedRoles={['Agent', 'Admin']}>
                  <AssignedTicketsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/agent/tickets/:id"
              element={
                <ProtectedRoute allowedRoles={['Agent', 'Admin']}>
                  <TicketDetailPage />
                </ProtectedRoute>
              }
            />

            {/* Admin Space */}
            <Route
              path="/admin/dashboard"
              element={
                <ProtectedRoute allowedRoles={['Admin']}>
                  <AdminDashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/admin/users"
              element={
                <ProtectedRoute allowedRoles={['Admin']}>
                  <UserManagementPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/admin/health"
              element={
                <ProtectedRoute allowedRoles={['Admin']}>
                  <SystemHealthPage />
                </ProtectedRoute>
              }
            />

            {/* Shared Protected Features */}
            <Route path="/tickets" element={<TicketsPage mode="all" />} />
            <Route path="/tickets/new" element={<CreateTicketPage />} />
            <Route path="/tickets/:id" element={<TicketDetailPage />} />
            <Route
              path="/tickets/escalations"
              element={
                <ProtectedRoute allowedRoles={['Agent', 'Admin']}>
                  <TicketsPage mode="escalations" />
                </ProtectedRoute>
              }
            />
            <Route path="/knowledge-base" element={<KnowledgeBasePage />} />

            {/* Access & Error Handlers */}
            <Route path="/forbidden" element={<ForbiddenPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
};
