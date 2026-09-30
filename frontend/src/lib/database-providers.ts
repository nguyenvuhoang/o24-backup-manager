export const DEFAULT_DATABASE_PORTS = {
  sqlserver: 1433,
  postgresql: 5432,
  oracle: 1521,
} as const;

export type DatabaseProvider = keyof typeof DEFAULT_DATABASE_PORTS;

export const DATABASE_PROVIDERS = {
  sqlserver: { displayName: 'Microsoft SQL Server', shortName: 'SQL Server', implemented: true },
  postgresql: { displayName: 'PostgreSQL', shortName: 'PostgreSQL', implemented: false },
  oracle: { displayName: 'Oracle', shortName: 'Oracle', implemented: false },
} as const;

interface PortConfiguration {
  provider: DatabaseProvider;
  port?: number | null | '';
  useNamedInstanceDiscovery?: boolean;
}

export function normalizeConnectionPort<T extends PortConfiguration>(form: T): Omit<T, 'port'> & { port: number | null } {
  const missing = form.port === null || form.port === undefined || form.port === '';
  const discovery = form.provider === 'sqlserver' && form.useNamedInstanceDiscovery;
  return { ...form, port: missing ? (discovery ? null : DEFAULT_DATABASE_PORTS[form.provider]) : form.port as number };
}

export function changeConnectionProvider<T extends PortConfiguration>(form: T, provider: DatabaseProvider): Omit<T, 'provider' | 'port'> & { provider: DatabaseProvider; port: number | null } {
  const custom = typeof form.port === 'number' && form.port !== DEFAULT_DATABASE_PORTS[form.provider];
  return {
    ...form,
    provider,
    port: custom ? form.port as number : DEFAULT_DATABASE_PORTS[provider],
    useNamedInstanceDiscovery: false,
  };
}
