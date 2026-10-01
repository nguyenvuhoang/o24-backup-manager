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

import type {
  BackupJob,
  BackupRun,
  BackupRuntime
} from '@/types/api';

const EMPTY_RUNTIME: BackupRuntime = {
  currentJobId: null,
  queuedCount: 0,
  jobs: [],
  schedules: []
};

export default function Home() {
  const [view, setView] =
    useState('overview');

  const [jobs, setJobs] =
    useState<BackupJob[]>([]);

  const [runs, setRuns] =
    useState<BackupRun[]>([]);

  const [runtime, setRuntime] =
    useState<BackupRuntime>(
      EMPTY_RUNTIME
    );

  const [editingJob, setEditingJob] =
    useState<BackupJob | null>(null);

  const [error, setError] =
    useState('');

  const [saved, setSaved] =
    useState(false);

  const load = useCallback(
    async () => {
      try {
        const [
          jobsResult,
          runsResult,
          runtimeResult
        ] = await Promise.all([
          api.jobs(),
          api.runs(),
          api.runtime()
        ]);

        setJobs(jobsResult);
        setRuns(runsResult);
        setRuntime(runtimeResult);

        setError('');
      } catch (err) {
        console.error(err);

        setError(
          'Không kết nối được Backend .NET. Kiểm tra dịch vụ tại cổng 5088.'
        );
      }
    },
    []
  );

  useRunEvents(load);

  useEffect(() => {
    void load();

    const timer =
      window.setInterval(
        () => {
          void load();
        },
        5000
      );

    return () => {
      window.clearInterval(timer);
    };
  }, [load]);

  async function run(id: string) {
    const existingRuntime =
      runtime.jobs.find(
        (item) => item.jobId === id
      );

    if (existingRuntime) {
      return;
    }

    setRuntime(
      (current) => {
        if (
          current.jobs.some(
            (item) => item.jobId === id
          )
        ) {
          return current;
        }

        const hasRunningJob =
          Boolean(current.currentJobId);

        const queuePosition =
          hasRunningJob
            ? current.queuedCount + 1
            : null;

        return {
          ...current,

          queuedCount:
            hasRunningJob
              ? current.queuedCount + 1
              : current.queuedCount,

          jobs: [
            ...current.jobs,
            {
              jobId: id,
              state:
                hasRunningJob
                  ? 'Queued'
                  : 'Running',
              queuePosition
            }
          ]
        };
      }
    );

    setError('');

    try {
      await api.run(id);

      window.setTimeout(
        () => {
          void load();
        },
        200
      );

      window.setTimeout(
        () => {
          void load();
        },
        800
      );
    } catch (err) {
      console.error(err);

      setError(
        err instanceof Error
          ? err.message
          : 'Không thể khởi chạy backup job.'
      );

      void load();
    }
  }

  function createJob() {
    setEditingJob(null);
    setView('create');
  }

  function editJob(job: BackupJob) {
    if (!job.id) {
      return;
    }

    setEditingJob(job);
    setView('edit');
  }

  function leaveJobForm() {
    setEditingJob(null);
    setView('overview');
  }

  return (
    <div className="flex min-h-screen flex-col md:flex-row">
      <Sidebar
        view={view}
        setView={(nextView) => {
          if (nextView !== 'create' && nextView !== 'edit') {
            setEditingJob(null);
          }

          setView(nextView);
        }}
      />

      <main className="w-full min-w-0 max-w-[1700px] flex-1 p-4 sm:p-6 xl:p-9">
        {saved && (
          <div className="mb-5">
            <Notification
              notice={{
                kind: 'success',
                message:
                  'Đã lưu backup job thành công.'
              }}
              onDismiss={() =>
                setSaved(false)
              }
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
            runtime={runtime}
            onRun={run}
            onCreate={createJob}
            onEdit={editJob}
          />
        )}

        {(view === 'create' || view === 'edit') && (
          <JobForm
            initialJob={
              view === 'edit'
                ? editingJob
                : null
            }
            onManage={() =>
              setView('settings')
            }
            onBack={leaveJobForm}
            onSaved={() => {
              setSaved(true);
              setEditingJob(null);
              setView('overview');
              void load();
            }}
          />
        )}

        {view === 'history' && (
          <History runs={runs} />
        )}

        {view === 'settings' && (
          <ConnectionManager />
        )}
      </main>
    </div>
  );
}
