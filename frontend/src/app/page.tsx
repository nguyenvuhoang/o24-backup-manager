'use client';

import { useCallback, useEffect, useState } from 'react';

import { Sidebar } from '@/components/sidebar';
import { Notification } from '@/components/notification';

import { ConnectionManager } from '@/features/connections/connection-manager';
import { Dashboard } from '@/features/dashboard/dashboard';
import { JobForm } from '@/features/jobs/job-form';
import { History } from '@/features/history/history';

import { api } from '@/lib/api/client';
import { useRunEvents } from '@/lib/signalr/use-run-events';

import type { BackupJob, BackupRun } from '@/types/api';

export default function Home() {
  const [view, setView] = useState('overview');

  const [jobs, setJobs] = useState<BackupJob[]>([]);
  const [runs, setRuns] = useState<BackupRun[]>([]);

  const [error, setError] = useState('');
  const [saved, setSaved] = useState(false);

  const [runningJobIds, setRunningJobIds] = useState<Set<string>>(
    () => new Set()
  );

  const load = useCallback(async () => {
    try {
      const [jobsResult, runsResult] = await Promise.all([
        api.jobs(),
        api.runs(),
      ]);

      setJobs(jobsResult);
      setRuns(runsResult);

      const currentlyRunning = new Set(
        runsResult
          .filter(
            (run: BackupRun) =>
              run.status === 'Running' || run.status === 'Queued'
          )
          .map((run: BackupRun) => run.jobId)
      );

      setRunningJobIds(currentlyRunning);

      setError('');
    } catch (err) {
      console.error(err);

      setError(
        'Không kết nối được Backend .NET. Kiểm tra dịch vụ tại cổng 5088.'
      );
    }
  }, []);

  useRunEvents(load);

  useEffect(() => {
    void load();

    const timer = window.setInterval(() => {
      void load();
    }, 5000);

    return () => {
      window.clearInterval(timer);
    };
  }, [load]);

  async function run(id: string) {
    if (runningJobIds.has(id)) {
      return;
    }

    setRunningJobIds((current) => {
      const next = new Set(current);
      next.add(id);
      return next;
    });

    setError('');

    try {
      await api.run(id);

      // Pull immediately so the new Queued/Running run appears
      // without waiting for the regular 5-second polling cycle.
      window.setTimeout(() => {
        void load();
      }, 300);

      window.setTimeout(() => {
        void load();
      }, 1000);
    } catch (err) {
      console.error(err);

      setRunningJobIds((current) => {
        const next = new Set(current);
        next.delete(id);
        return next;
      });

      setError(
        err instanceof Error
          ? err.message
          : 'Không thể khởi chạy backup job.'
      );

      void load();
    }
  }

  return (
    <div className="flex min-h-screen flex-col md:flex-row">
      <Sidebar view={view} setView={setView} />

      <main className="w-full min-w-0 max-w-[1700px] flex-1 p-4 sm:p-6 xl:p-9">
        {saved && (
          <div className="mb-5">
            <Notification
              notice={{
                kind: 'success',
                message: 'Đã lưu backup job thành công.',
              }}
              onDismiss={() => setSaved(false)}
            />
          </div>
        )}

        {error && (
          <div className="mb-5 rounded-xl border border-red-200 bg-red-50 p-4 text-red-700">
            {error}
          </div>
        )}

        {view === 'overview' && (
          <Dashboard
            jobs={jobs}
            runs={runs}
            runningJobIds={runningJobIds}
            onRun={run}
            onCreate={() => setView('create')}
          />
        )}

        {view === 'create' && (
          <JobForm
            onManage={() => setView('settings')}
            onBack={() => setView('overview')}
            onSaved={() => {
              setSaved(true);
              setView('overview');
              void load();
            }}
          />
        )}

        {view === 'history' && <History runs={runs} />}

        {view === 'settings' && <ConnectionManager />}
      </main>
    </div>
  );
}