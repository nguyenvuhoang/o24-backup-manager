export function validateJob(job) {
  const errors = {};
  if (!job.name?.trim()) errors.name = 'Tên job là bắt buộc.';
  if (!job.databases?.length) errors.databases = 'Chọn ít nhất một database.';
  if (!job.backupDirectory?.trim()) errors.backupDirectory = 'Thư mục backup là bắt buộc.';
  if (!Number.isInteger(job.retentionDays) || job.retentionDays < 1) errors.retentionDays = 'Giữ file ít nhất 1 ngày (số nguyên).';
  if (!job.connectionId) errors.connectionId = 'Cần kết nối database server.';
  return errors;
}
