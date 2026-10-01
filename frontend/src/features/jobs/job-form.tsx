'use client';

import {
  useEffect,
  useRef,
  useState,
  type FormEvent
} from 'react';

import { api, ApiError } from '@/lib/api/client';
import { validateJob } from '@/lib/job-validation.mjs';
import { LatestRequest } from '@/lib/latest-request';
import {
  Notification,
  type Notice
} from '@/components/notification';

import type {
  BackupJob,
  DatabaseDiscoveryResult,
  SavedDatabaseConnection
} from '@/types/api';

import {
  selectableNames
} from '@/lib/database-selection';

import {
  BasicInformationCard,
  type JobDetails
} from './basic-information-card';

import { DatabaseConnectionCard } from './database-connection-card';
import { DatabaseConnectionModal } from './database-connection-modal';
import { DatabaseSelector } from './database-selector';
import { BackupJobSummary } from './backup-job-summary';

interface JobFormProps {
  initialJob?: BackupJob | null;
  onSaved: () => void;
  onBack: () => void;
  onManage: () => void;
}

export function JobForm({
  initialJob = null,
  onSaved,
  onBack,
  onManage
}: JobFormProps) {
  const isEditing =
    Boolean(initialJob?.id);

  const draftId =
    useRef<string | null>(
      initialJob?.id ?? null
    );

  const [job, setJob] =
    useState<JobDetails>({
      name:
        initialJob?.name ?? '',
      backupDirectory:
        initialJob?.backupDirectory ?? '',
      retentionDays:
        initialJob?.retentionDays ?? 2
    });

  const [connection, setConnection] =
    useState<SavedDatabaseConnection | null>(
      null
    );

  const [connections, setConnections] =
    useState<SavedDatabaseConnection[]>([]);

  const [connecting, setConnecting] =
    useState(false);

  const selectionRequest =
    useRef<AbortController | null>(
      null
    );

  const testRequests =
    useRef(new LatestRequest());

  const [discovery, setDiscovery] =
    useState<DatabaseDiscoveryResult | null>(
      null
    );

  const [connectionId, setConnectionId] =
    useState<string | null>(
      initialJob?.connectionId ?? null
    );

  const [selected, setSelected] =
    useState<string[]>(
      initialJob?.databases ?? []
    );

  const [modal, setModal] =
    useState(false);

  const [saving, setSaving] =
    useState(false);

  const [notice, setNotice] =
    useState<Notice | null>(null);

  const [errors, setErrors] =
    useState<Record<string, string>>({});

  useEffect(() => {
    let active = true;

    async function initialize() {
      try {
        const items =
          await api.connections();

        if (!active) {
          return;
        }

        setConnections(items);

        if (
          initialJob?.connectionId
        ) {
          const savedConnection =
            items.find(
              (item) =>
                item.id ===
                initialJob.connectionId
            );

          if (!savedConnection) {
            setNotice({
              kind: 'error',
              message:
                'Không tìm thấy kết nối SQL Server đã lưu của backup job này.'
            });

            return;
          }

          await loadConnection(
            savedConnection,
            true,
            initialJob.databases
          );
        }
      } catch (error) {
        if (active) {
          setNotice({
            kind: 'error',
            message:
              error instanceof Error
                ? error.message
                : 'Không tải được danh sách kết nối.'
          });
        }
      }
    }

    void initialize();

    const tests =
      testRequests.current;

    return () => {
      active = false;
      selectionRequest.current?.abort();
      tests.cancel();
    };
    // The form is mounted for one create/edit target.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function disconnect() {
    testRequests.current.cancel();
    selectionRequest.current?.abort();

    setConnecting(false);
    setConnection(null);
    setDiscovery(null);
    setConnectionId(null);
    setSelected([]);
    setNotice(null);
  }

  async function loadConnection(
    saved: SavedDatabaseConnection,
    preserveSelection: boolean,
    selectionOverride?: string[]
  ) {
    testRequests.current.cancel();
    selectionRequest.current?.abort();

    const request =
      new AbortController();

    selectionRequest.current =
      request;

    setConnection(saved);
    setConnectionId(saved.id);
    setModal(false);
    setErrors({});

    if (!preserveSelection) {
      setDiscovery(null);
      setSelected([]);
    }

    setConnecting(true);

    setConnections((items) => [
      ...items.filter(
        (item) =>
          item.id !== saved.id
      ),
      saved
    ]);

    setNotice({
      kind: 'info',
      message: preserveSelection
        ? 'Đang tải lại danh sách database từ SQL Server...'
        : 'Đang tải database từ kết nối đã lưu...'
    });

    try {
      const result =
        await api.discoverSavedConnection(
          saved.id,
          request.signal
        );

      if (request.signal.aborted) {
        return;
      }

      const selectable =
        new Set(
          selectableNames(
            result.databases
          )
        );

      const previousSelection =
        selectionOverride
        ?? selected;

      const nextSelection =
        preserveSelection
          ? previousSelection.filter(
            (name) =>
              selectable.has(name)
          )
          : [];

      const removedSelected =
        preserveSelection
          ? previousSelection.filter(
            (name) =>
              !selectable.has(name)
          )
          : [];

      setSelected(
        nextSelection
      );

      setDiscovery(result);

      if (
        removedSelected.length > 0
      ) {
        setNotice({
          kind: 'success',
          message:
            `Đã tải lại ${result.databases.length} database từ ${result.server.name}. `
            + `Đã tự loại ${removedSelected.length} database không còn tồn tại/khả dụng: `
            + removedSelected.join(', ')
        });
      } else {
        setNotice({
          kind: 'success',
          message:
            preserveSelection
              ? `Đã tải lại ${result.databases.length} database từ ${result.server.name}. Các lựa chọn còn hợp lệ được giữ nguyên.`
              : `Đã kết nối ${result.server.name}. Tải được ${result.databases.length} database.`
        });
      }
    } catch (error) {
      if (!request.signal.aborted) {
        setNotice({
          kind: 'error',
          message:
            preserveSelection
              ? `Không thể tải lại database. ${error instanceof Error
                ? error.message
                : 'Danh sách lựa chọn hiện tại được giữ nguyên.'
              }`
              : `Kết nối đã lưu. ${error instanceof Error
                ? error.message
                : 'Không tải được database.'
              }`
        });
      }
    } finally {
      if (!request.signal.aborted) {
        setConnecting(false);
      }
    }
  }

  async function connected(
    saved: SavedDatabaseConnection
  ) {
    const sameConnection =
      connectionId === saved.id;

    await loadConnection(
      saved,
      sameConnection
    );
  }

  async function refreshDatabases() {
    if (!connection) {
      return;
    }

    await loadConnection(
      connection,
      true
    );
  }

  async function testSaved() {
    if (!connection) {
      return;
    }

    const request =
      testRequests.current.start();

    try {
      const result =
        await api.testSavedConnection(
          connection.id,
          request.signal
        );

      if (!request.signal.aborted) {
        setNotice({
          kind: 'success',
          message:
            `Kết nối thành công · ${result.serverName} · SQL Server ${result.version}`
        });
      }
    } catch (error) {
      if (!request.signal.aborted) {
        setNotice({
          kind: 'error',
          message:
            error instanceof Error
              ? error.message
              : 'Không thể kết nối.'
        });
      }
    }
  }

  async function save(
    event: FormEvent
  ) {
    event.preventDefault();

    if (saving) {
      return;
    }

    const validation =
      validateJob({
        ...job,
        databases: selected,
        connectionId:
          connection
            ? 'connected'
            : undefined
      });

    setErrors(validation);

    if (
      Object.keys(validation).length
      || !connectionId
      || !discovery
    ) {
      setNotice({
        kind: 'error',
        message:
          'Vui lòng kiểm tra các trường bắt buộc.'
      });

      return;
    }

    const currentlySelectable =
      new Set(
        selectableNames(
          discovery.databases
        )
      );

    const validSelected =
      selected.filter(
        (name) =>
          currentlySelectable.has(
            name
          )
      );

    if (
      validSelected.length !==
      selected.length
    ) {
      setSelected(
        validSelected
      );

      setNotice({
        kind: 'error',
        message:
          'Danh sách database đã thay đổi. Một số database không còn tồn tại hoặc không còn khả dụng đã được loại bỏ. Vui lòng kiểm tra lại trước khi lưu.'
      });

      return;
    }

    setSaving(true);

    setNotice({
      kind: 'info',
      message:
        isEditing
          ? 'Đang kiểm tra và lưu thay đổi...'
          : 'Đang kiểm tra và lưu cấu hình...'
    });

    try {
      draftId.current ??=
        crypto.randomUUID();

      await api.saveJob({
        id:
          draftId.current,
        name:
          job.name.trim(),
        connectionId,
        databases:
          validSelected,
        backupDirectory:
          job.backupDirectory.trim(),
        retentionDays:
          job.retentionDays,

        /*
         * Preserve fields that are not edited by this form.
         * This is especially important for schedule and the
         * SQL Server-side staging directory.
         */
        enabled:
          initialJob?.enabled
          ?? true,

        sqlServerBackupDirectory:
          initialJob
            ?.sqlServerBackupDirectory,

        schedule:
          initialJob?.schedule,

        sftp:
          initialJob?.sftp,

        telegram:
          initialJob?.telegram
      });

      onSaved();
    } catch (error) {
      if (
        error instanceof ApiError
        && [
          'CONNECTION_FAILED',
          'DISCOVERY_FAILED',
          'DATABASE_UNAVAILABLE',
          'NOT_FOUND',
          'CONNECTION_CHANGED'
        ].includes(
          error.code ?? ''
        )
      ) {
        disconnect();
      }

      if (
        error instanceof ApiError
        && error.errors
      ) {
        setErrors(
          Object.fromEntries(
            Object.entries(
              error.errors
            ).map(
              ([key, values]) => [
                key,
                values[0]
              ]
            )
          )
        );
      }

      setNotice({
        kind: 'error',
        message:
          error instanceof Error
            ? error.message
            : 'Không thể lưu job.'
      });
    } finally {
      setSaving(false);
    }
  }

  return (
    <section>
      <header className="mb-7">
        <button
          type="button"
          onClick={onBack}
          disabled={saving}
          className="mb-4 text-sm text-slate-500 hover:text-blue-700"
        >
          ← Quay lại
        </button>

        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">
          {isEditing
            ? 'Chỉnh sửa Backup Job'
            : 'Tạo Backup Job'}
        </h1>

        <p className="mt-2 text-sm text-slate-500">
          {isEditing
            ? 'Cập nhật database và cấu hình job hiện tại'
            : 'SQL Server → Verify → ZIP → SCP → Telegram'}
        </p>
      </header>

      <div className="mb-5">
        <Notification
          notice={notice}
          onDismiss={() =>
            setNotice(null)
          }
        />
      </div>

      <form
        onSubmit={save}
        noValidate
      >
        <fieldset
          disabled={saving}
          className="grid min-w-0 gap-6 lg:grid-cols-[minmax(0,7fr)_minmax(290px,3fr)]"
        >
          <div className="min-w-0 space-y-6">
            <BasicInformationCard
              value={job}
              onChange={setJob}
              errors={errors}
            />

            <div className="panel space-y-3 p-5">
              <label className="block text-sm font-semibold">
                Kết nối SQL Server

                <select
                  className="job-input mt-2"
                  value={
                    connectionId ?? ''
                  }
                  onChange={(e) => {
                    const item =
                      connections.find(
                        (candidate) =>
                          candidate.id ===
                          e.target.value
                      );

                    if (item) {
                      void loadConnection(
                        item,
                        false
                      );
                    } else {
                      disconnect();
                    }
                  }}
                >
                  <option value="">
                    Chọn kết nối đã lưu
                  </option>

                  {connections.map(
                    (item) => (
                      <option
                        key={item.id}
                        value={item.id}
                      >
                        {item.name}
                      </option>
                    )
                  )}
                </select>
              </label>

              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  className="job-button"
                  onClick={onManage}
                >
                  Quản lý
                </button>

                <button
                  type="button"
                  className="job-button"
                  onClick={() => {
                    disconnect();
                    setModal(true);
                  }}
                >
                  + Thêm kết nối
                </button>

                {connection && (
                  <>
                    <button
                      type="button"
                      className="job-button"
                      disabled={connecting}
                      onClick={() =>
                        void testSaved()
                      }
                    >
                      Kiểm tra
                    </button>

                    <button
                      type="button"
                      className="job-button"
                      disabled={connecting}
                      onClick={() =>
                        void refreshDatabases()
                      }
                    >
                      {connecting
                        ? 'Đang tải...'
                        : 'Tải lại database'}
                    </button>
                  </>
                )}
              </div>
            </div>

            <DatabaseConnectionCard
              connection={
                connection
                  ? {
                    ...connection,
                    password: null
                  }
                  : null
              }
              server={
                discovery?.server
                ?? null
              }
              onConfigure={() =>
                setModal(true)
              }
              onDisconnect={
                disconnect
              }
              error={
                errors.connectionId
              }
            />

            <DatabaseSelector
              key={
                discovery?.server.name
                ?? 'disconnected'
              }
              databases={
                discovery?.databases
                ?? []
              }
              selected={selected}
              connected={!!discovery}
              onChange={setSelected}
              error={
                errors.databases
              }
            />
          </div>

          <BackupJobSummary
            job={job}
            connection={
              connection
                ? {
                  ...connection,
                  password: null
                }
                : null
            }
            server={
              discovery?.server
              ?? null
            }
            selected={selected}
            saving={
              saving
              || connecting
            }
          />
        </fieldset>
      </form>

      {modal && (
        <DatabaseConnectionModal
          initial={connection}
          onClose={() =>
            setModal(false)
          }
          onConnected={(saved) =>
            void connected(saved)
          }
        />
      )}
    </section>
  );
}
