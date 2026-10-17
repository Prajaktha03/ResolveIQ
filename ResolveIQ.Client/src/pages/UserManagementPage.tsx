import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { usersApi, ApiClientError } from '../services/api';
import { User } from '../types';
import { RoleBadge } from '../components/Badge';
import {
  Shield,
  RefreshCw,
  AlertCircle,
  CheckCircle2,
  Search,
  Users as UsersIcon,
  X,
  AlertTriangle,
  UserCheck,
} from 'lucide-react';

interface RoleChangeTarget {
  user: User;
  newRole: 'Employee' | 'Agent';
}

export const UserManagementPage: React.FC = () => {
  const [users, setUsers] = useState<User[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  // Search & Filter state
  const [searchTerm, setSearchTerm] = useState('');
  const [roleFilter, setRoleFilter] = useState<string>('');

  // Confirmation modal state
  const [pendingRoleChange, setPendingRoleChange] = useState<RoleChangeTarget | null>(null);
  const [isSubmittingRole, setIsSubmittingRole] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);

  const fetchUsers = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await usersApi.getAll();
      setUsers(data);
    } catch (err: unknown) {
      if (err instanceof ApiClientError && err.isForbidden) {
        setError('Access denied: You do not have permission to manage system users.');
      } else {
        const msg = err instanceof Error ? err.message : 'Failed to retrieve system user directory.';
        setError(msg);
      }
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchUsers();
  }, [fetchUsers]);

  // Client-side filtering across the fetched users directory
  const filteredUsers = useMemo(() => {
    return users.filter((u) => {
      const matchesSearch =
        !searchTerm.trim() ||
        u.fullName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        u.email.toLowerCase().includes(searchTerm.toLowerCase()) ||
        `#${u.id}`.includes(searchTerm);

      const matchesRole = !roleFilter || u.role === roleFilter;

      return matchesSearch && matchesRole;
    });
  }, [users, searchTerm, roleFilter]);

  const initiateRoleChange = (user: User, newRole: 'Employee' | 'Agent') => {
    setModalError(null);
    setPendingRoleChange({ user, newRole });
  };

  const cancelRoleChange = () => {
    if (isSubmittingRole) return;
    setPendingRoleChange(null);
    setModalError(null);
  };

  const handleConfirmRoleChange = async () => {
    if (!pendingRoleChange) return;

    setIsSubmittingRole(true);
    setModalError(null);
    setError(null);
    setSuccessMessage(null);

    const { user: targetUser, newRole } = pendingRoleChange;

    try {
      const res = await usersApi.updateRole(targetUser.id, newRole);

      // Only update local state after server confirms success
      setUsers((prev) =>
        prev.map((u) => (u.id === targetUser.id ? { ...u, role: newRole } : u))
      );

      setSuccessMessage(
        res.message || `User #${targetUser.id} (${targetUser.fullName}) role updated to ${newRole}.`
      );
      setPendingRoleChange(null);
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        setModalError(err.message);
      } else {
        const msg = err instanceof Error ? err.message : 'Failed to update user role.';
        setModalError(msg);
      }
    } finally {
      setIsSubmittingRole(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header Banner */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 bg-slate-900 p-6 rounded-2xl border border-slate-800">
        <div>
          <div className="flex items-center gap-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-purple-500/10 text-purple-400 border border-purple-500/20">
              <Shield className="w-3.5 h-3.5" />
              Role Governance
            </span>
          </div>
          <h2 className="text-xl font-bold text-white mt-1">User Management</h2>
          <p className="text-sm text-slate-400">
            Audit system registered accounts, promote employees to support agents, and inspect security scopes.
          </p>
        </div>
        <button
          onClick={fetchUsers}
          disabled={isLoading}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition self-start sm:self-auto disabled:opacity-50"
        >
          <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          <span>Refresh Directory</span>
        </button>
      </div>

      {/* Global Alerts */}
      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm flex items-center gap-3">
          <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      {successMessage && (
        <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-300 text-sm flex items-center gap-3">
          <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
          <span>{successMessage}</span>
        </div>
      )}

      {/* Search and Filters Bar */}
      <div className="flex flex-col sm:flex-row gap-3">
        <div className="relative flex-1">
          <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400" />
          <input
            type="text"
            placeholder="Search users by name, email, or #ID..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>
        <select
          value={roleFilter}
          onChange={(e) => setRoleFilter(e.target.value)}
          className="px-3.5 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        >
          <option value="">All Roles</option>
          <option value="Employee">Employees</option>
          <option value="Agent">Agents</option>
          <option value="Admin">Administrators</option>
        </select>
      </div>

      {/* Users Table */}
      <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm">
        <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <UsersIcon className="w-4 h-4 text-purple-400" />
            <h3 className="font-semibold text-white text-sm">Registered Accounts</h3>
          </div>
          <span className="text-xs text-slate-400">
            {filteredUsers.length} of {users.length} accounts
          </span>
        </div>

        {isLoading ? (
          <div className="py-16 flex flex-col items-center justify-center gap-3 text-slate-400">
            <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
            <p className="text-sm">Retrieving registered users...</p>
          </div>
        ) : filteredUsers.length === 0 ? (
          <div className="py-16 text-center text-slate-400 text-sm">
            No accounts matched your search and filter criteria.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-slate-800/80 bg-slate-950/40 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
                  <th className="py-3 px-6">ID</th>
                  <th className="py-3 px-6">Full Name</th>
                  <th className="py-3 px-6">Email Address</th>
                  <th className="py-3 px-6">Security Role</th>
                  <th className="py-3 px-6">Registered Date</th>
                  <th className="py-3 px-6 text-right">Role Governance Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 text-sm">
                {filteredUsers.map((u) => {
                  return (
                    <tr key={u.id} className="hover:bg-slate-800/30 transition-colors">
                      <td className="py-3.5 px-6 font-mono text-xs text-slate-400">#{u.id}</td>
                      <td className="py-3.5 px-6 font-medium text-slate-200">{u.fullName}</td>
                      <td className="py-3.5 px-6 text-slate-300 font-mono text-xs">{u.email}</td>
                      <td className="py-3.5 px-6">
                        <RoleBadge role={u.role} />
                      </td>
                      <td className="py-3.5 px-6 text-xs text-slate-400">
                        {new Date(u.createdAt).toLocaleDateString()}
                      </td>
                      <td className="py-3.5 px-6 text-right">
                        {u.role === 'Admin' ? (
                          <span
                            className="text-xs text-slate-500 italic inline-flex items-center gap-1"
                            title="Admin accounts cannot be changed through this endpoint to protect security access."
                          >
                            <Shield className="w-3 h-3 text-purple-400/50" />
                            Admin Protected
                          </span>
                        ) : u.role === 'Employee' ? (
                          <button
                            onClick={() => initiateRoleChange(u, 'Agent')}
                            className="px-3 py-1 rounded-lg text-xs font-semibold text-indigo-400 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/30 transition"
                          >
                            Promote to Agent
                          </button>
                        ) : (
                          <button
                            onClick={() => initiateRoleChange(u, 'Employee')}
                            className="px-3 py-1 rounded-lg text-xs font-semibold text-slate-300 bg-slate-800 hover:bg-slate-700 border border-slate-700 transition"
                          >
                            Revert to Employee
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Role Change Confirmation Modal */}
      {pendingRoleChange && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
              <h3 className="font-semibold text-white text-base flex items-center gap-2">
                <AlertTriangle className="w-5 h-5 text-amber-400" />
                Confirm Role Change
              </h3>
              <button
                onClick={cancelRoleChange}
                disabled={isSubmittingRole}
                className="text-slate-400 hover:text-white disabled:opacity-50"
                aria-label="Close"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="p-6 space-y-4">
              {modalError && (
                <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 shrink-0" />
                  <span>{modalError}</span>
                </div>
              )}

              <p className="text-sm text-slate-300">
                Are you sure you want to change the role of{' '}
                <strong className="text-white">{pendingRoleChange.user.fullName}</strong> (
                <span className="font-mono text-xs text-slate-400">{pendingRoleChange.user.email}</span>
                )?
              </p>

              <div className="p-3.5 rounded-xl bg-slate-800/60 border border-slate-700 flex items-center justify-between text-xs">
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase">Current Role</span>
                  <span className="font-semibold text-slate-200">{pendingRoleChange.user.role}</span>
                </div>
                <span className="text-slate-500">→</span>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase">New Role</span>
                  <span className="font-semibold text-indigo-400">{pendingRoleChange.newRole}</span>
                </div>
              </div>

              <p className="text-xs text-slate-400">
                {pendingRoleChange.newRole === 'Agent'
                  ? 'Promoting to Agent grants this user ticket assignment capabilities, internal troubleshooting tools, and SLA queue access.'
                  : 'Reverting to Employee restricts access to the standard Employee Portal and their submitted tickets only.'}
              </p>

              <div className="pt-2 flex justify-end gap-3">
                <button
                  type="button"
                  onClick={cancelRoleChange}
                  disabled={isSubmittingRole}
                  className="px-4 py-2 rounded-xl text-sm font-medium text-slate-300 hover:bg-slate-800 disabled:opacity-50 transition"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleConfirmRoleChange}
                  disabled={isSubmittingRole}
                  className="px-5 py-2 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 transition flex items-center gap-2"
                >
                  {isSubmittingRole ? (
                    <>
                      <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                      <span>Updating...</span>
                    </>
                  ) : (
                    <>
                      <UserCheck className="w-4 h-4" />
                      <span>Confirm Change</span>
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
