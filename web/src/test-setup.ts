import { TestBed } from '@angular/core/testing';
import {
  REALTIME_CONNECTION_FACTORY,
  RealtimeConnection,
} from './app/core/realtime/realtime-connection';

/**
 * Unit tests must never open a real socket. By default every test gets a connection that is "still connecting" forever: views then
 * behave exactly as they do when the live channel is unavailable (they poll), which is what most tests assume.
 * Tests of the real-time behaviour itself override this token with a controllable fake.
 */
class NeverConnectedConnection implements RealtimeConnection {
  start = () => new Promise<void>(() => undefined);
  stop = () => Promise.resolve();
  onLotChanged = () => undefined;
  invoke = () => Promise.resolve();
  onReconnecting = () => undefined;
  onReconnected = () => undefined;
  onClose = () => undefined;
}

beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [
      { provide: REALTIME_CONNECTION_FACTORY, useValue: () => new NeverConnectedConnection() },
    ],
  });
});
