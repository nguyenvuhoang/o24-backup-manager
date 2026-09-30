'use client';
import { useEffect } from 'react';
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr';

export function useRunEvents(onChanged: () => void) {
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/runs', { transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling })
      .withAutomaticReconnect()
      .build();
    const refresh = () => onChanged();
    connection.on('RunStarted', refresh);
    connection.on('StageCompleted', refresh);
    connection.on('RunCompleted', refresh);
    connection.onreconnected(refresh);
    connection.start().catch(() => undefined);
    return () => { connection.stop().catch(() => undefined); };
  }, [onChanged]);
}
