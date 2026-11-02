import { InjectionToken, inject } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';

/**
 * The small part of a SignalR connection the app needs. LiveUpdates depends on THIS, not on the library, so tests drive it with
 * a fake and never open a socket.
 */
export interface RealtimeConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  /** Registers a handler for a server message with a single Guid argument. */
  onLotChanged(handler: (lotId: string) => void): void;
  invoke(method: 'JoinLot' | 'LeaveLot', lotId: string): Promise<void>;
  invoke(method: 'JoinAllLots' | 'LeaveAllLots'): Promise<void>;
  onReconnecting(handler: () => void): void;
  onReconnected(handler: () => void): void;
  onClose(handler: () => void): void;
}

export const REALTIME_CONNECTION_FACTORY = new InjectionToken<() => RealtimeConnection>(
  'REALTIME_CONNECTION_FACTORY',
  {
    providedIn: 'root',
    factory: () => {
      const auth = inject(AuthService);
      return () => {
        const hub = new HubConnectionBuilder()
          // Same origin as the app (dev proxy / nginx forward /hubs to the API). The token is read each time a connection is made.
          .withUrl('/hubs/availability', { accessTokenFactory: () => auth.token() ?? '' })
          // After a successful connection, retry quickly at first and then back off, forever.
          .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: (ctx) =>
              [0, 2000, 5000, 10_000][ctx.previousRetryCount] ?? 30_000,
          })
          .configureLogging(LogLevel.Warning)
          .build();

        const adapter: RealtimeConnection = {
          start: () => hub.start(),
          stop: () => hub.stop(),
          onLotChanged: (handler) => hub.on('LotChanged', handler),
          invoke: (method: string, ...args: string[]) =>
            hub.invoke(method, ...args).then(() => undefined),
          onReconnecting: (handler) => hub.onreconnecting(() => handler()),
          onReconnected: (handler) => hub.onreconnected(() => handler()),
          onClose: (handler) => hub.onclose(() => handler()),
        };
        return adapter;
      };
    },
  },
);
