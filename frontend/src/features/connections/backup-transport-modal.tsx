'use client';

import {
    useEffect,
    useRef,
    useState
} from 'react';

import {
    Modal
} from '@/components/modal';

import {
    Notification,
    type Notice
} from '@/components/notification';

import {
    api,
    ApiError
} from '@/lib/api/client';

import type {
    BackupTransportSettings,
    SavedDatabaseConnection
} from '@/types/api';

import {
    FormField
} from '@/features/jobs/form-elements';

const defaultTransport: BackupTransportSettings = {
    enabled: true,
    sshHost: '',
    sshPort: 22,
    sshUser: 'root',
    sshPrivateKeyPath: '',
    rcloneRemote: '',
    rootPath: 'DATABASE',
    verifyAfterUpload: true,
    connectTimeoutSeconds: 30,
    uploadTimeoutMinutes: 240
};

export function BackupTransportModal({
    connection,
    onClose,
    onSaved
}: {
    connection: SavedDatabaseConnection;
    onClose: () => void;
    onSaved: (
        saved: SavedDatabaseConnection
    ) => void;
}) {
    const [form, setForm] =
        useState<BackupTransportSettings>(
            () => ({
                ...defaultTransport,
                sshHost: connection.host,
                ...connection.backupTransport
            })
        );

    const [busy, setBusy] =
        useState<'save' | 'test' | null>(
            null
        );

    const [notice, setNotice] =
        useState<Notice | null>(
            null
        );

    const [errors, setErrors] =
        useState<Record<string, string[]>>(
            {}
        );

    const controller =
        useRef<AbortController | null>(
            null
        );

    const formRef =
        useRef<HTMLFormElement>(
            null
        );

    useEffect(
        () =>
            () =>
                controller.current?.abort(),
        []
    );

    function change<
        K extends keyof BackupTransportSettings
    >(
        key: K,
        value: BackupTransportSettings[K]
    ) {
        setForm(
            current => ({
                ...current,
                [key]: value
            })
        );

        setNotice(null);
        setErrors({});
    }

    function normalize():
        BackupTransportSettings {
        return {
            ...form,

            sshHost:
                form.sshHost.trim(),

            sshPort:
                Number.isFinite(form.sshPort)
                    ? form.sshPort
                    : 22,

            sshUser:
                form.sshUser.trim(),

            sshPrivateKeyPath:
                form.sshPrivateKeyPath.trim(),

            rcloneRemote:
                form.rcloneRemote.trim(),

            rootPath:
                form.rootPath.trim()
                || 'DATABASE',

            connectTimeoutSeconds:
                Number.isFinite(
                    form.connectTimeoutSeconds
                )
                    ? form.connectTimeoutSeconds
                    : 30,

            uploadTimeoutMinutes:
                Number.isFinite(
                    form.uploadTimeoutMinutes
                )
                    ? form.uploadTimeoutMinutes
                    : 240
        };
    }

    async function save() {
        if (
            busy
            || !formRef.current?.reportValidity()
        ) {
            return;
        }

        setBusy('save');
        setNotice(null);
        setErrors({});

        try {
            const normalized =
                normalize();

            setForm(normalized);

            const saved =
                await api.saveBackupTransport(
                    connection.id,
                    normalized
                );

            setNotice({
                kind: 'success',
                message:
                    'Đã lưu cấu hình Backup Transport.'
            });

            onSaved(saved);
        }
        catch (error) {
            setNotice({
                kind: 'error',
                message:
                    error instanceof Error
                        ? error.message
                        : 'Không thể lưu Backup Transport.'
            });

            if (error instanceof ApiError) {
                setErrors(
                    error.errors ?? {}
                );
            }
        }
        finally {
            setBusy(null);
        }
    }

    async function test() {
        if (
            busy
            || !formRef.current?.reportValidity()
        ) {
            return;
        }

        //
        // Endpoint test reads the SAVED transport.
        // Save first so the values being tested are exactly
        // the values currently displayed in this form.
        //
        setBusy('test');
        setNotice({
            kind: 'info',
            message:
                'Đang lưu cấu hình và kiểm tra SSH + rclone + Google Drive...'
        });
        setErrors({});

        const request =
            new AbortController();

        controller.current =
            request;

        try {
            const normalized =
                normalize();

            setForm(normalized);

            const saved =
                await api.saveBackupTransport(
                    connection.id,
                    normalized
                );

            if (request.signal.aborted) {
                return;
            }

            const result =
                await api.testBackupTransport(
                    connection.id,
                    request.signal
                );

            if (request.signal.aborted) {
                return;
            }

            setNotice({
                kind: 'success',
                message:
                    result.message
            });

            onSaved(saved);
        }
        catch (error) {
            if (!request.signal.aborted) {
                setNotice({
                    kind: 'error',
                    message:
                        error instanceof Error
                            ? error.message
                            : 'Không thể kiểm tra Backup Transport.'
                });

                if (error instanceof ApiError) {
                    setErrors(
                        error.errors ?? {}
                    );
                }
            }
        }
        finally {
            if (!request.signal.aborted) {
                setBusy(null);
            }
        }
    }

    function close() {
        if (busy) {
            return;
        }

        controller.current?.abort();
        onClose();
    }

    const disabled =
        !form.enabled;

    return (
        <Modal
            title="Backup Transport"
            subtitle={`${connection.name} · ${connection.host}`}
            onClose={close}
        >
            <form
                ref={formRef}
                onSubmit={event => {
                    event.preventDefault();
                    void save();
                }}
            >
                <fieldset
                    disabled={!!busy}
                    className="space-y-5 p-5 sm:p-6"
                >
                    <label className="flex items-start gap-3 rounded-xl border border-slate-200 bg-slate-50 p-4">
                        <input
                            type="checkbox"
                            className="mt-1"
                            checked={form.enabled}
                            onChange={event =>
                                change(
                                    'enabled',
                                    event.target.checked
                                )
                            }
                        />

                        <span>
                            <span className="block text-sm font-semibold text-slate-800">
                                Enable remote backup
                            </span>

                            <span className="mt-1 block text-xs leading-5 text-slate-500">
                                Sau khi SQL Server backup và VERIFYONLY thành công,
                                BackupManager sẽ SSH vào server này và dùng rclone
                                để upload lên Google Drive.
                            </span>
                        </span>
                    </label>

                    <div className="rounded-xl border border-slate-200 p-4">
                        <div className="mb-4">
                            <h3 className="text-sm font-bold text-slate-800">
                                SSH
                            </h3>

                            <p className="mt-1 text-xs text-slate-500">
                                Đây phải là server đang chứa file .bak của SQL Server.
                            </p>
                        </div>

                        <div className="grid gap-5 sm:grid-cols-[1fr_140px]">
                            <FormField
                                label="SSH Host"
                                name="sshHost"
                                required={form.enabled}
                                disabled={disabled}
                                autoComplete="off"
                                placeholder="192.168.1.90"
                                maxLength={255}
                                value={form.sshHost}
                                onChange={event =>
                                    change(
                                        'sshHost',
                                        event.target.value
                                    )
                                }
                                error={errors.sshHost?.[0]}
                            />

                            <FormField
                                label="SSH Port"
                                name="sshPort"
                                type="number"
                                required={form.enabled}
                                disabled={disabled}
                                min={1}
                                max={65535}
                                step={1}
                                value={form.sshPort || ''}
                                onChange={event =>
                                    change(
                                        'sshPort',
                                        event.target.valueAsNumber
                                    )
                                }
                                error={errors.sshPort?.[0]}
                            />
                        </div>

                        <div className="mt-5">
                            <FormField
                                label="SSH User"
                                name="sshUser"
                                required={form.enabled}
                                disabled={disabled}
                                autoComplete="off"
                                placeholder="root"
                                maxLength={128}
                                value={form.sshUser}
                                onChange={event =>
                                    change(
                                        'sshUser',
                                        event.target.value
                                    )
                                }
                                error={errors.sshUser?.[0]}
                            />
                        </div>

                        <div className="mt-5">
                            <FormField
                                label="SSH Private Key Path"
                                name="sshPrivateKeyPath"
                                disabled={disabled}
                                autoComplete="off"
                                placeholder="Để trống để dùng global BackupManager key"
                                maxLength={1024}
                                value={form.sshPrivateKeyPath}
                                onChange={event =>
                                    change(
                                        'sshPrivateKeyPath',
                                        event.target.value
                                    )
                                }
                                hint="Đây là đường dẫn trên máy Windows chạy Backend, không phải đường dẫn trên SQL Server."
                                error={errors.sshPrivateKeyPath?.[0]}
                            />
                        </div>
                    </div>

                    <div className="rounded-xl border border-slate-200 p-4">
                        <div className="mb-4">
                            <h3 className="text-sm font-bold text-slate-800">
                                Google Drive / rclone
                            </h3>

                            <p className="mt-1 text-xs text-slate-500">
                                Remote phải tồn tại trong rclone config của SSH user trên server.
                            </p>
                        </div>

                        <div className="grid gap-5 sm:grid-cols-2">
                            <FormField
                                label="Rclone Remote"
                                name="rcloneRemote"
                                required={form.enabled}
                                disabled={disabled}
                                autoComplete="off"
                                placeholder="jitsadmin"
                                maxLength={128}
                                value={form.rcloneRemote}
                                onChange={event =>
                                    change(
                                        'rcloneRemote',
                                        event.target.value
                                    )
                                }
                                error={errors.rcloneRemote?.[0]}
                            />

                            <FormField
                                label="Google Drive Root"
                                name="rootPath"
                                required={form.enabled}
                                disabled={disabled}
                                autoComplete="off"
                                placeholder="DATABASE"
                                maxLength={512}
                                value={form.rootPath}
                                onChange={event =>
                                    change(
                                        'rootPath',
                                        event.target.value
                                    )
                                }
                                error={errors.rootPath?.[0]}
                            />
                        </div>

                        <label className="mt-5 flex items-center gap-2 text-sm">
                            <input
                                type="checkbox"
                                checked={form.verifyAfterUpload}
                                disabled={disabled}
                                onChange={event =>
                                    change(
                                        'verifyAfterUpload',
                                        event.target.checked
                                    )
                                }
                            />

                            Verify after upload using rclone check --one-way
                        </label>
                    </div>

                    <details className="rounded-xl border border-slate-200 p-4">
                        <summary className="cursor-pointer text-sm font-semibold">
                            Tùy chọn nâng cao
                        </summary>

                        <div className="mt-5 grid gap-5 sm:grid-cols-2">
                            <FormField
                                label="Connect Timeout (giây)"
                                name="connectTimeoutSeconds"
                                type="number"
                                required
                                disabled={disabled}
                                min={1}
                                max={300}
                                step={1}
                                value={form.connectTimeoutSeconds || ''}
                                onChange={event =>
                                    change(
                                        'connectTimeoutSeconds',
                                        event.target.valueAsNumber
                                    )
                                }
                                error={errors.connectTimeoutSeconds?.[0]}
                            />

                            <FormField
                                label="Upload Timeout (phút)"
                                name="uploadTimeoutMinutes"
                                type="number"
                                required
                                disabled={disabled}
                                min={1}
                                max={10080}
                                step={1}
                                value={form.uploadTimeoutMinutes || ''}
                                onChange={event =>
                                    change(
                                        'uploadTimeoutMinutes',
                                        event.target.valueAsNumber
                                    )
                                }
                                error={errors.uploadTimeoutMinutes?.[0]}
                            />
                        </div>
                    </details>

                    <div className="rounded-xl bg-slate-50 p-4 text-xs leading-5 text-slate-600">
                        <strong>Luồng:</strong>{' '}
                        SQL Server {connection.host}
                        {' → '}
                        SSH {form.sshHost || '...'}
                        {' → '}
                        {form.rcloneRemote || 'remote'}:
                        {form.rootPath || 'DATABASE'}
                    </div>
                </fieldset>

                <div className="px-5 pb-5 sm:px-6">
                    <Notification
                        notice={notice}
                    />
                </div>

                <footer className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 bg-slate-50 px-5 py-4 sm:px-6">
                    <button
                        type="button"
                        className="job-button"
                        disabled={
                            !!busy
                            || !form.enabled
                        }
                        onClick={() =>
                            void test()
                        }
                    >
                        {busy === 'test'
                            ? 'Đang kiểm tra...'
                            : 'Test backup storage'}
                    </button>

                    <div className="flex gap-3">
                        <button
                            type="button"
                            className="job-button"
                            disabled={!!busy}
                            onClick={close}
                        >
                            Hủy
                        </button>

                        <button
                            type="submit"
                            className="job-primary"
                            disabled={!!busy}
                        >
                            {busy === 'save'
                                ? 'Đang lưu...'
                                : 'Lưu cấu hình'}
                        </button>
                    </div>
                </footer>
            </form>
        </Modal>
    );
}