'use client';

import { useMemo, useState } from 'react';

import type {
  BackupJob,
  BackupRun,
  BackupRuntime,
  DatabaseBackupRun,
  RunLog,
  RunStatus,
  RuntimeJobStatus
} from '@/types/api';

interface DashboardProps {
  jobs: BackupJob[];
  runs: BackupRun[];
  runtime: BackupRuntime;
  onRun: (id: string) => void;
  onCreate: () => void;
  onEdit: (job: BackupJob) => void;
}

function getLatestRun(
  jobId: string,
  runs: BackupRun[]
) {
  return runs
    .filter(
      (run) =>
        run.jobId === jobId
    )
    .sort(
      (a, b) =>
        new Date(b.createdAt).getTime()
        - new Date(a.createdAt).getTime()
    )[0];
}

function statusLabel(
  status?: RunStatus
) {
  switch (status) {
    case 'Queued':
      return 'Đang chờ';

    case 'Running':
      return 'Đang chạy';

    case 'Succeeded':
      return 'Thành công';

    case 'Failed':
      return 'Thất bại';

    case 'Cancelled':
      return 'Đã hủy';

    default:
      return 'Chưa chạy';
  }
}

function statusClass(
  status?: RunStatus
) {
  switch (status) {
    case 'Running':
      return 'bg-[#eef3fc] text-[#4f6fae] ring-[#d9e3f4]';

    case 'Queued':
      return 'bg-[#fff8ea] text-[#a47638] ring-[#f1e3c6]';

    case 'Succeeded':
      return 'bg-[#edf8f3] text-[#347f65] ring-[#d1ebdf]';

    case 'Failed':
      return 'bg-[#fdf1f3] text-[#ad5965] ring-[#f1d8dd]';

    case 'Cancelled':
      return 'bg-[#f2f4f7] text-[#667085] ring-[#e4e7ec]';

    default:
      return 'bg-[#f4f5f7] text-[#667085] ring-[#e4e7ec]';
  }
}

function databaseStatusIcon(
  database: DatabaseBackupRun
) {
  switch (database.status) {
    case 'Succeeded':
      return (
        <span className="flex h-7 w-7 items-center justify-center rounded-full bg-[#e7f6ef] text-sm font-bold text-[#3b9273]">
          ✓
        </span>
      );

    case 'Failed':
      return (
        <span className="flex h-7 w-7 items-center justify-center rounded-full bg-[#fbeaec] text-sm font-bold text-[#bb6370]">
          !
        </span>
      );

    case 'Running':
      return (
        <span className="flex h-7 w-7 items-center justify-center rounded-full bg-[#e8effa] text-sm font-bold text-[#5275b8]">
          ●
        </span>
      );

    default:
      return (
        <span className="flex h-7 w-7 items-center justify-center rounded-full bg-[#f2f4f7] text-sm text-[#98a2b3]">
          ○
        </span>
      );
  }
}

function formatBytes(
  value?: number | null
) {
  if (
    value === null
    || value === undefined
    || value <= 0
  ) {
    return '—';
  }

  const units = [
    'B',
    'KB',
    'MB',
    'GB',
    'TB'
  ];

  let size = value;
  let unitIndex = 0;

  while (
    size >= 1024
    && unitIndex < units.length - 1
  ) {
    size /= 1024;
    unitIndex += 1;
  }

  const decimals =
    unitIndex >= 3
      ? 2
      : 1;

  return `${size.toFixed(decimals)} ${units[unitIndex]}`;
}

function formatDate(
  value?: string | null
) {
  if (!value) {
    return '—';
  }

  const date =
    new Date(value);

  if (
    Number.isNaN(
      date.getTime()
    )
  ) {
    return value;
  }

  return new Intl.DateTimeFormat(
    'vi-VN',
    {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    }
  ).format(date);
}

function formatTime(
  value?: string | null
) {
  if (!value) {
    return '--:--:--';
  }

  const date =
    new Date(value);

  if (
    Number.isNaN(
      date.getTime()
    )
  ) {
    return '--:--:--';
  }

  return new Intl.DateTimeFormat(
    'vi-VN',
    {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    }
  ).format(date);
}

function formatDuration(
  startedAt?: string | null,
  finishedAt?: string | null
) {
  if (!startedAt) {
    return '—';
  }

  const start =
    new Date(
      startedAt
    ).getTime();

  const finish =
    finishedAt
      ? new Date(
        finishedAt
      ).getTime()
      : Date.now();

  if (
    Number.isNaN(start)
    || Number.isNaN(finish)
    || finish < start
  ) {
    return '—';
  }

  const totalSeconds =
    Math.floor(
      (finish - start) / 1000
    );

  if (totalSeconds < 60) {
    return `${totalSeconds}s`;
  }

  const minutes =
    Math.floor(
      totalSeconds / 60
    );

  const seconds =
    totalSeconds % 60;

  if (minutes < 60) {
    return `${minutes}m ${seconds}s`;
  }

  const hours =
    Math.floor(
      minutes / 60
    );

  const remainingMinutes =
    minutes % 60;

  return `${hours}h ${remainingMinutes}m`;
}

function isVerboseSqlProgress(
  message: string
) {
  const normalized =
    message.trim();

  if (!normalized) {
    return true;
  }

  if (
    normalized.includes(
      'percent processed'
    )
    && normalized.includes(
      'BACKUP DATABASE successfully processed'
    )
  ) {
    return true;
  }

  return false;
}

function cleanLogs(
  logs?: RunLog[]
) {
  return (
    logs ?? []
  ).filter(
    (log) =>
      !isVerboseSqlProgress(
        log.message
      )
  );
}

function logDotClass(
  log: RunLog
) {
  switch (log.level) {
    case 'Error':
      return 'bg-[#d57b86]';

    case 'Warning':
      return 'bg-[#d4a45c]';

    default:
      if (
        log.stage === 'Completed'
      ) {
        return 'bg-[#63ad91]';
      }

      if (
        log.stage === 'BackingUp'
        || log.stage === 'Verifying'
        || log.stage === 'Transferring'
        || log.stage === 'VerifyingUpload'
      ) {
        return 'bg-[#7695ce]';
      }

      return 'bg-[#8996aa]';
  }
}

function getRuntimeStatus(
  jobId: string | undefined,
  runtime: BackupRuntime
): RuntimeJobStatus | undefined {
  if (!jobId) {
    return undefined;
  }

  return runtime.jobs.find(
    (item) =>
      item.jobId === jobId
  );
}

function runtimeStatusLabel(
  status: RuntimeJobStatus
) {
  if (
    status.state === 'Queued'
  ) {
    return status.queuePosition
      ? `Đang chờ #${status.queuePosition}`
      : 'Đang chờ';
  }

  return 'Đang chạy';
}

function runtimeStatusClass(
  status: RuntimeJobStatus
) {
  return status.state === 'Queued'
    ? statusClass('Queued')
    : statusClass('Running');
}

function formatSchedule(
  job: BackupJob
) {
  if (
    !job.enabled
    || !job.schedule?.enabled
  ) {
    return 'Không bật lịch';
  }

  return `Hàng ngày ${job.schedule.time} · ${job.schedule.timeZone}`;
}

function getTimeZoneDateParts(
  date: Date,
  timeZone: string
) {
  try {
    const formatter =
      new Intl.DateTimeFormat(
        'en-CA',
        {
          timeZone,
          year: 'numeric',
          month: '2-digit',
          day: '2-digit',
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
          hourCycle: 'h23'
        }
      );

    const parts =
      formatter.formatToParts(
        date
      );

    const get =
      (type: Intl.DateTimeFormatPartTypes) =>
        Number(
          parts.find(
            (part) =>
              part.type === type
          )?.value ?? '0'
        );

    return {
      year: get('year'),
      month: get('month'),
      day: get('day'),
      hour: get('hour'),
      minute: get('minute'),
      second: get('second')
    };
  } catch {
    return null;
  }
}

function formatNextRun(
  job: BackupJob
) {
  if (
    !job.enabled
    || !job.schedule?.enabled
  ) {
    return '—';
  }

  const match =
    /^(\d{2}):(\d{2})$/.exec(
      job.schedule.time
    );

  if (!match) {
    return 'Lịch không hợp lệ';
  }

  const scheduleHour =
    Number(match[1]);

  const scheduleMinute =
    Number(match[2]);

  if (
    scheduleHour < 0
    || scheduleHour > 23
    || scheduleMinute < 0
    || scheduleMinute > 59
  ) {
    return 'Lịch không hợp lệ';
  }

  const now =
    new Date();

  const parts =
    getTimeZoneDateParts(
      now,
      job.schedule.timeZone
    );

  if (!parts) {
    return `${job.schedule.time} · ${job.schedule.timeZone}`;
  }

  const currentMinutes =
    parts.hour * 60
    + parts.minute;

  const scheduleMinutes =
    scheduleHour * 60
    + scheduleMinute;

  //
  // We only need the local calendar date shown to the user.
  // Add one local calendar day when today's scheduled time
  // has already passed.
  //
  const localCalendar =
    new Date(
      Date.UTC(
        parts.year,
        parts.month - 1,
        parts.day
      )
    );

  if (
    currentMinutes >=
    scheduleMinutes
  ) {
    localCalendar.setUTCDate(
      localCalendar.getUTCDate()
      + 1
    );
  }

  const day =
    String(
      localCalendar.getUTCDate()
    ).padStart(
      2,
      '0'
    );

  const month =
    String(
      localCalendar.getUTCMonth()
      + 1
    ).padStart(
      2,
      '0'
    );

  const year =
    localCalendar.getUTCFullYear();

  return `${day}/${month}/${year} ${job.schedule.time}`;
}

export function Dashboard({
  jobs,
  runs,
  runtime,
  onRun,
  onCreate,
  onEdit
}: DashboardProps) {
  const [
    expandedRunId,
    setExpandedRunId
  ] =
    useState<string | null>(
      null
    );

  const latestRuns =
    useMemo(
      () => {
        return jobs
          .map(
            (job) => {
              if (!job.id) {
                return undefined;
              }

              return getLatestRun(
                job.id,
                runs
              );
            }
          )
          .filter(
            (
              run
            ): run is BackupRun =>
              Boolean(run)
          );
      },
      [jobs, runs]
    );

  //
  // Runtime is authoritative for active work.
  //
  const runningCount =
    runtime.jobs.filter(
      (item) =>
        item.state === 'Running'
    ).length;

  const queuedCount =
    runtime.jobs.filter(
      (item) =>
        item.state === 'Queued'
    ).length;

  const successCount =
    latestRuns.filter(
      (run) =>
        run.status === 'Succeeded'
    ).length;

  const failedCount =
    latestRuns.filter(
      (run) =>
        run.status === 'Failed'
    ).length;

  const currentJob =
    runtime.currentJobId
      ? jobs.find(
        (job) =>
          job.id ===
          runtime.currentJobId
      )
      : undefined;

  return (
    <>
      <header className="mb-7 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-3xl font-bold text-[#263247]">
            Tổng quan
          </h1>

          <p className="mt-1 text-[#7a8699]">
            Theo dõi backup, lịch chạy và hàng đợi trên server này
          </p>
        </div>

        <button
          onClick={onCreate}
          className="
            rounded-xl border border-[#4f73c8]
            bg-[#4f73c8]
            px-5 py-3
            font-semibold text-white
            shadow-[0_3px_10px_rgba(79,115,200,0.13)]
            transition
            hover:border-[#4669ba]
            hover:bg-[#4669ba]
          "
        >
          + Tạo job
        </button>
      </header>

      <div className="mb-6 grid grid-cols-2 gap-4 xl:grid-cols-5">
        <div className="panel p-5">
          <div className="text-sm text-[#7a8699]">
            Jobs
          </div>

          <div className="mt-2 text-3xl font-bold text-[#344054]">
            {jobs.length}
          </div>
        </div>

        <div className="panel p-5">
          <div className="text-sm text-[#7a8699]">
            Đang chạy
          </div>

          <div className="mt-2 text-3xl font-bold text-[#5879b8]">
            {runningCount}
          </div>
        </div>

        <div className="panel p-5">
          <div className="text-sm text-[#7a8699]">
            Đang chờ
          </div>

          <div className="mt-2 text-3xl font-bold text-[#a47638]">
            {queuedCount}
          </div>
        </div>

        <div className="panel p-5">
          <div className="text-sm text-[#7a8699]">
            Thành công
          </div>

          <div className="mt-2 text-3xl font-bold text-[#3d9274]">
            {successCount}
          </div>
        </div>

        <div className="panel p-5">
          <div className="text-sm text-[#7a8699]">
            Thất bại
          </div>

          <div className="mt-2 text-3xl font-bold text-[#bb6873]">
            {failedCount}
          </div>
        </div>
      </div>

      {(runtime.currentJobId || runtime.queuedCount > 0) && (
        <div className="mb-6 rounded-2xl border border-[#dce5f4] bg-[#f7f9fd] p-5">
          <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
            <div>
              <div className="text-xs font-semibold uppercase tracking-wide text-[#8a95a6]">
                Backup worker
              </div>

              <div className="mt-1 font-semibold text-[#405b8f]">
                {currentJob
                  ? `Đang chạy: ${currentJob.name}`
                  : 'Worker đang xử lý'}
              </div>
            </div>

            <div className="text-sm text-[#667085]">
              {runtime.queuedCount > 0
                ? `${runtime.queuedCount} job đang chờ`
                : 'Không có job đang chờ'}
            </div>
          </div>
        </div>
      )}

      <section className="panel overflow-hidden">
        <div className="border-b border-[#edf0f4] p-6">
          <h2 className="text-lg font-semibold text-[#344054]">
            Backup jobs
          </h2>

          <p className="mt-1 text-sm text-[#8a95a6]">
            Running/Queued lấy trực tiếp từ backup worker.
            Kết quả hoàn tất lấy từ lịch sử run.
          </p>
        </div>

        {jobs.length === 0 ? (
          <div className="py-14 text-center text-[#98a2b3]">
            Chưa có backup job.
          </div>
        ) : (
          <div>
            {jobs.map(
              (job) => {
                const jobId =
                  job.id;

                const latestRun =
                  jobId
                    ? getLatestRun(
                      jobId,
                      runs
                    )
                    : undefined;

                const runtimeStatus =
                  getRuntimeStatus(
                    jobId,
                    runtime
                  );

                const isActive =
                  Boolean(
                    runtimeStatus
                  );

                const total =
                  latestRun?.totalDatabases
                  ?? latestRun?.databases?.length
                  ?? job.databases.length;

                const completed =
                  latestRun?.completedDatabases
                  ?? latestRun?.databases?.filter(
                    (database) =>
                      database.status === 'Succeeded'
                      || database.status === 'Failed'
                      || database.status === 'Cancelled'
                  ).length
                  ?? 0;

                const succeeded =
                  latestRun?.succeededDatabases
                  ?? latestRun?.databases?.filter(
                    (database) =>
                      database.status === 'Succeeded'
                  ).length
                  ?? 0;

                const failed =
                  latestRun?.failedDatabases
                  ?? latestRun?.databases?.filter(
                    (database) =>
                      database.status === 'Failed'
                  ).length
                  ?? 0;

                const progress =
                  total > 0
                    ? Math.min(
                      100,
                      Math.round(
                        (
                          completed
                          / total
                        ) * 100
                      )
                    )
                    : 0;

                const expanded =
                  latestRun?.id
                  === expandedRunId;

                const visibleLogs =
                  cleanLogs(
                    latestRun?.logs
                  );

                const displayStatus =
                  runtimeStatus
                    ? runtimeStatus.state
                    : latestRun?.status;

                return (
                  <div
                    key={
                      jobId
                      ?? job.name
                    }
                    className="border-b border-[#edf0f4] last:border-b-0"
                  >
                    <div className="p-5 sm:p-6">
                      <div className="flex flex-col gap-5 xl:flex-row xl:items-center">
                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-3">
                            <div className="text-lg font-semibold text-[#344054]">
                              {job.name}
                            </div>

                            <span
                              className={[
                                'rounded-full px-3 py-1 text-xs font-semibold ring-1 ring-inset',
                                job.enabled
                                  ? 'bg-[#edf8f3] text-[#347f65] ring-[#d1ebdf]'
                                  : 'bg-[#f2f4f7] text-[#667085] ring-[#e4e7ec]'
                              ].join(' ')}
                            >
                              {job.enabled
                                ? 'Enabled'
                                : 'Disabled'}
                            </span>

                            {runtimeStatus ? (
                              <span
                                className={[
                                  'rounded-full px-3 py-1 text-xs font-semibold ring-1 ring-inset',
                                  runtimeStatusClass(
                                    runtimeStatus
                                  )
                                ].join(' ')}
                              >
                                {runtimeStatusLabel(
                                  runtimeStatus
                                )}
                              </span>
                            ) : latestRun ? (
                              <span
                                className={[
                                  'rounded-full px-3 py-1 text-xs font-semibold ring-1 ring-inset',
                                  statusClass(
                                    latestRun.status
                                  )
                                ].join(' ')}
                              >
                                {statusLabel(
                                  latestRun.status
                                )}
                              </span>
                            ) : null}
                          </div>

                          <div className="mt-2 text-sm text-[#7a8699]">
                            {job.databases.length} database
                            {' · '}
                            {job.sqlServer?.server
                              ?? 'Kết nối đã lưu'}
                          </div>

                          <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                            <MiniInfo
                              label="Schedule"
                              value={formatSchedule(
                                job
                              )}
                            />

                            <MiniInfo
                              label="Next run"
                              value={formatNextRun(
                                job
                              )}
                            />

                            <MiniInfo
                              label="Last run"
                              value={
                                latestRun
                                  ? formatDate(
                                    latestRun.startedAt
                                    ?? latestRun.createdAt
                                  )
                                  : '—'
                              }
                            />

                            <MiniInfo
                              label="Worker"
                              value={
                                runtimeStatus?.state
                                  === 'Running'
                                  ? 'Running now'
                                  : runtimeStatus?.state
                                    === 'Queued'
                                    ? runtimeStatus.queuePosition
                                      ? `Queue #${runtimeStatus.queuePosition}`
                                      : 'Queued'
                                    : 'Idle'
                              }
                            />
                          </div>

                          {runtimeStatus?.state === 'Queued' && (
                            <div className="mt-3 rounded-xl border border-[#efe1c4] bg-[#fffaf0] px-4 py-3 text-sm text-[#8b6935]">
                              {runtimeStatus.queuePosition
                                ? `Job đang ở vị trí #${runtimeStatus.queuePosition} trong hàng đợi.`
                                : 'Job đang chờ trong hàng đợi.'}

                              {currentJob && (
                                <>
                                  {' '}
                                  Đang chờ{' '}
                                  <span className="font-semibold">
                                    {currentJob.name}
                                  </span>{' '}
                                  hoàn tất.
                                </>
                              )}
                            </div>
                          )}

                          {latestRun && (
                            <div className="mt-4">
                              <div className="mb-2 flex flex-wrap items-center justify-between gap-2 text-sm">
                                <div className="font-medium text-[#475467]">
                                  {completed}/{total} database
                                </div>

                                <div className="text-[#8a95a6]">
                                  {progress}%
                                </div>
                              </div>

                              <div className="h-2 overflow-hidden rounded-full bg-[#edf0f4]">
                                <div
                                  className={[
                                    'h-full rounded-full transition-all duration-500',
                                    displayStatus === 'Failed'
                                      ? 'bg-[#cb7480]'
                                      : displayStatus === 'Succeeded'
                                        ? 'bg-[#62ad8f]'
                                        : displayStatus === 'Queued'
                                          ? 'bg-[#d4a45c]'
                                          : 'bg-[#6f8fc9]'
                                  ].join(' ')}
                                  style={{
                                    width:
                                      runtimeStatus?.state
                                        === 'Queued'
                                        ? '0%'
                                        : `${progress}%`
                                  }}
                                />
                              </div>

                              <div className="mt-3 flex flex-wrap gap-x-5 gap-y-2 text-sm">
                                <span className="text-[#398267]">
                                  ✓ {succeeded} thành công
                                </span>

                                {failed > 0 && (
                                  <span className="text-[#ad5965]">
                                    ! {failed} thất bại
                                  </span>
                                )}

                                {runtimeStatus?.state === 'Running'
                                  && latestRun.currentDatabase && (
                                    <span className="font-medium text-[#5576b4]">
                                      Đang xử lý:{' '}
                                      {latestRun.currentDatabase}
                                    </span>
                                  )}

                                {runtimeStatus?.state === 'Running'
                                  && latestRun.currentStage && (
                                    <span className="text-[#7a8699]">
                                      Stage:{' '}
                                      {latestRun.currentStage}
                                    </span>
                                  )}

                                {!runtimeStatus
                                  && latestRun.currentStage && (
                                    <span className="text-[#7a8699]">
                                      Stage:{' '}
                                      {latestRun.currentStage}
                                    </span>
                                  )}
                              </div>
                            </div>
                          )}
                        </div>

                        <div className="flex shrink-0 flex-wrap items-center gap-2">
                          <button
                            type="button"
                            disabled={!jobId || isActive}
                            onClick={() => {
                              if (jobId) {
                                onEdit(job);
                              }
                            }}
                            className={[
                              'rounded-xl border px-4 py-2 text-sm font-medium transition',
                              !jobId || isActive
                                ? 'cursor-not-allowed border-[#e4e7ec] bg-[#f2f4f7] text-[#98a2b3]'
                                : 'border-[#dce2ea] bg-white text-[#475467] hover:border-[#ccd5e1] hover:bg-[#fafbfc]'
                            ].join(' ')}
                            title={
                              isActive
                                ? 'Không thể sửa job khi đang chạy hoặc đang chờ'
                                : 'Chỉnh sửa backup job'
                            }
                          >
                            Edit
                          </button>

                          {latestRun && (
                            <button
                              onClick={() =>
                                setExpandedRunId(
                                  expanded
                                    ? null
                                    : latestRun.id
                                )
                              }
                              className="
                                rounded-xl border border-[#dce2ea]
                                bg-white px-4 py-2
                                text-sm font-medium text-[#475467]
                                transition
                                hover:border-[#ccd5e1]
                                hover:bg-[#fafbfc]
                              "
                            >
                              {expanded
                                ? 'Ẩn chi tiết'
                                : 'Chi tiết'}
                            </button>
                          )}

                          <button
                            disabled={
                              !jobId
                              || !job.enabled
                              || isActive
                            }
                            onClick={() => {
                              if (jobId) {
                                onRun(jobId);
                              }
                            }}
                            className={[
                              'rounded-xl px-4 py-2 text-sm font-semibold transition',
                              isActive
                                ? 'cursor-not-allowed bg-[#eef3fc] text-[#6682b8]'
                                : !job.enabled
                                  ? 'cursor-not-allowed bg-[#f2f4f7] text-[#98a2b3]'
                                  : 'border border-[#dce2ea] bg-white text-[#475467] hover:bg-[#fafbfc]'
                            ].join(' ')}
                          >
                            {runtimeStatus?.state
                              === 'Running'
                              ? 'Đang chạy...'
                              : runtimeStatus?.state
                                === 'Queued'
                                ? runtimeStatus.queuePosition
                                  ? `Đang chờ #${runtimeStatus.queuePosition}`
                                  : 'Đang chờ...'
                                : 'Run now'}
                          </button>
                        </div>
                      </div>
                    </div>

                    {expanded
                      && latestRun && (
                        <div className="border-t border-[#edf0f4] bg-[#fafbfc] p-5 sm:p-6">
                          <div className="grid gap-4 lg:grid-cols-4">
                            <InfoCard
                              label="Bắt đầu"
                              value={formatDate(
                                latestRun.startedAt
                              )}
                            />

                            <InfoCard
                              label="Kết thúc"
                              value={formatDate(
                                latestRun.finishedAt
                              )}
                            />

                            <InfoCard
                              label="Thời gian"
                              value={formatDuration(
                                latestRun.startedAt,
                                latestRun.finishedAt
                              )}
                            />

                            <div className="rounded-xl border border-[#e5e9ef] bg-white p-4">
                              <div className="text-xs font-medium uppercase tracking-wide text-[#98a2b3]">
                                Run ID
                              </div>

                              <div
                                className="mt-2 truncate font-mono text-xs text-[#667085]"
                                title={
                                  latestRun.id
                                }
                              >
                                {latestRun.id}
                              </div>
                            </div>
                          </div>

                          <div className="mt-5 rounded-xl border border-[#e5e9ef] bg-white p-4">
                            <div className="text-xs font-semibold uppercase tracking-wide text-[#98a2b3]">
                              SQL Server backup location
                            </div>

                            <div className="mt-2 break-all font-mono text-sm text-[#596579]">
                              {latestRun.sqlServerRunDirectory
                                ?? latestRun.sqlServerBackupDirectory
                                ?? '—'}
                            </div>
                          </div>

                          {latestRun.remoteBackupDirectory && (
                            <div className="mt-4 rounded-xl border border-[#e5e9ef] bg-white p-4">
                              <div className="text-xs font-semibold uppercase tracking-wide text-[#98a2b3]">
                                Remote backup location
                              </div>

                              <div className="mt-2 break-all font-mono text-sm text-[#596579]">
                                {latestRun.remoteBackupDirectory}
                              </div>
                            </div>
                          )}

                          {latestRun.error && (
                            <div className="mt-5 rounded-xl border border-[#f0d7dc] bg-[#fdf3f4] p-4">
                              <div className="text-sm font-semibold text-[#a8515e]">
                                Run error
                              </div>

                              <pre className="mt-2 whitespace-pre-wrap break-words font-mono text-xs text-[#ad5965]">
                                {latestRun.error}
                              </pre>
                            </div>
                          )}

                          <div className="mt-6">
                            <div className="mb-3 flex items-center justify-between">
                              <h3 className="font-semibold text-[#344054]">
                                Databases
                              </h3>

                              <div className="text-sm text-[#8a95a6]">
                                {completed}/{total}
                              </div>
                            </div>

                            <div className="overflow-x-auto rounded-xl border border-[#e5e9ef] bg-white">
                              <table className="min-w-full text-left text-sm">
                                <thead className="bg-[#f8f9fb] text-xs uppercase tracking-wide text-[#8a95a6]">
                                  <tr>
                                    <th className="px-4 py-3">
                                      Database
                                    </th>

                                    <th className="px-4 py-3">
                                      Status
                                    </th>

                                    <th className="px-4 py-3">
                                      Stage
                                    </th>

                                    <th className="px-4 py-3">
                                      Size
                                    </th>

                                    <th className="px-4 py-3">
                                      Duration
                                    </th>

                                    <th className="px-4 py-3">
                                      Backup file
                                    </th>
                                  </tr>
                                </thead>

                                <tbody className="divide-y divide-[#edf0f4]">
                                  {(latestRun.databases ?? []).map(
                                    (database) => (
                                      <tr
                                        key={
                                          database.database
                                        }
                                        className="align-top transition hover:bg-[#fbfcfd]"
                                      >
                                        <td className="px-4 py-3">
                                          <div className="flex items-center gap-3">
                                            {databaseStatusIcon(
                                              database
                                            )}

                                            <div>
                                              <div className="font-semibold text-[#344054]">
                                                {database.database}
                                              </div>

                                              {database.error && (
                                                <div className="mt-1 max-w-md whitespace-pre-wrap text-xs text-[#ad5965]">
                                                  {database.error}
                                                </div>
                                              )}
                                            </div>
                                          </div>
                                        </td>

                                        <td className="px-4 py-3">
                                          <span
                                            className={[
                                              'rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ring-inset',
                                              statusClass(
                                                database.status
                                              )
                                            ].join(' ')}
                                          >
                                            {statusLabel(
                                              database.status
                                            )}
                                          </span>
                                        </td>

                                        <td className="px-4 py-3 text-[#667085]">
                                          {database.stage}
                                        </td>

                                        <td className="whitespace-nowrap px-4 py-3 text-[#667085]">
                                          {formatBytes(
                                            database.backupSize
                                          )}
                                        </td>

                                        <td className="whitespace-nowrap px-4 py-3 text-[#667085]">
                                          {formatDuration(
                                            database.startedAt,
                                            database.finishedAt
                                          )}
                                        </td>

                                        <td className="px-4 py-3">
                                          {database.backupPath ? (
                                            <div
                                              className="max-w-md break-all font-mono text-xs text-[#7a8699]"
                                              title={
                                                database.backupPath
                                              }
                                            >
                                              {database.backupPath}
                                            </div>
                                          ) : (
                                            <span className="text-[#98a2b3]">
                                              —
                                            </span>
                                          )}
                                        </td>
                                      </tr>
                                    )
                                  )}

                                  {(latestRun.databases?.length
                                    ?? 0) === 0 && (
                                      <tr>
                                        <td
                                          colSpan={6}
                                          className="px-4 py-8 text-center text-[#98a2b3]"
                                        >
                                          Run cũ chưa có dữ liệu database chi tiết.
                                        </td>
                                      </tr>
                                    )}
                                </tbody>
                              </table>
                            </div>
                          </div>

                          <div className="mt-6">
                            <div className="mb-3 flex items-center justify-between">
                              <div>
                                <h3 className="font-semibold text-[#344054]">
                                  Live log
                                </h3>

                                <p className="mt-1 text-xs text-[#98a2b3]">
                                  Hiển thị các sự kiện chính.
                                  SQL progress chi tiết đã được
                                  ẩn để dễ theo dõi.
                                </p>
                              </div>

                              <div className="text-xs text-[#98a2b3]">
                                {visibleLogs.length} entries
                              </div>
                            </div>

                            <div
                              className="
                                max-h-[430px] overflow-auto
                                rounded-xl border border-[#253249]
                                bg-[#182235]
                                p-3
                                font-mono text-xs
                                shadow-inner
                              "
                            >
                              {visibleLogs.length === 0 ? (
                                <div className="px-2 py-3 text-[#7f8ca1]">
                                  Chưa có log cho run này.
                                </div>
                              ) : (
                                visibleLogs.map(
                                  (
                                    log,
                                    index
                                  ) => (
                                    <div
                                      key={`${log.timestamp}-${index}`}
                                      className="
                                        flex gap-3
                                        border-b border-white/[0.055]
                                        px-2 py-2.5
                                        last:border-0
                                      "
                                    >
                                      <div className="mt-[7px] shrink-0">
                                        <span
                                          className={[
                                            'block h-1.5 w-1.5 rounded-full',
                                            logDotClass(
                                              log
                                            )
                                          ].join(' ')}
                                        />
                                      </div>

                                      <div className="w-[70px] shrink-0 text-[#77859a]">
                                        {formatTime(
                                          log.timestamp
                                        )}
                                      </div>

                                      <div className="w-[86px] shrink-0 text-[#93a6c8]">
                                        {log.stage}
                                      </div>

                                      <div className="w-[100px] shrink-0 truncate font-semibold text-[#8fc3ad]">
                                        {log.database
                                          ?? 'SYSTEM'}
                                      </div>

                                      <div
                                        className={[
                                          'min-w-0 flex-1 whitespace-pre-wrap break-words',
                                          log.level === 'Error'
                                            ? 'text-[#e0a0a8]'
                                            : log.level === 'Warning'
                                              ? 'text-[#ddbc83]'
                                              : 'text-[#c2cad7]'
                                        ].join(' ')}
                                      >
                                        {log.message}
                                      </div>
                                    </div>
                                  )
                                )
                              )}
                            </div>
                          </div>
                        </div>
                      )}
                  </div>
                );
              }
            )}
          </div>
        )}
      </section>
    </>
  );
}

function MiniInfo({
  label,
  value
}: {
  label: string;
  value: string;
}) {
  return (
    <div className="rounded-xl border border-[#edf0f4] bg-[#fafbfc] px-3 py-2.5">
      <div className="text-[11px] font-semibold uppercase tracking-wide text-[#98a2b3]">
        {label}
      </div>

      <div className="mt-1 truncate text-sm font-medium text-[#596579]">
        {value}
      </div>
    </div>
  );
}

function InfoCard({
  label,
  value
}: {
  label: string;
  value: string;
}) {
  return (
    <div className="rounded-xl border border-[#e5e9ef] bg-white p-4">
      <div className="text-xs font-medium uppercase tracking-wide text-[#98a2b3]">
        {label}
      </div>

      <div className="mt-2 text-sm font-semibold text-[#475467]">
        {value}
      </div>
    </div>
  );
}