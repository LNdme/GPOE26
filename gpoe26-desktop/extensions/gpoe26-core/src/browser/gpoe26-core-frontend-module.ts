import { RpcProxyFactory } from '@theia/core/lib/common';
import { WebSocketConnectionProvider } from '@theia/core/lib/browser/messaging';
import { ContainerModule } from '@theia/core/shared/inversify';
import { LOCAL_STORE_PATH, LocalStore } from '../common/store-protocol';
import { HarnessClient, HarnessConfig } from './harness-client';
import { SyncService } from './sync-service';

/**
 * Le socle, côté interface.
 *
 * Les adresses des services viennent de la configuration de l'application et non du code :
 * une école qui héberge son propre harness ne doit pas avoir à recompiler.
 */
export default new ContainerModule(bind => {
    bind(LocalStore)
        .toDynamicValue(({ container }) =>
            container.get(WebSocketConnectionProvider)
                .createProxy<LocalStore>(LOCAL_STORE_PATH, new RpcProxyFactory<LocalStore>()))
        .inSingletonScope();

    bind(HarnessConfig).toConstantValue({
        harnessUrl: readEndpoint('gpoe26.harnessUrl', 'http://localhost:5200'),
        userUrl: readEndpoint('gpoe26.userUrl', 'http://localhost:5100')
    });

    bind(HarnessClient).toSelf().inSingletonScope();
    bind(SyncService).toSelf().inSingletonScope();
});

/**
 * Lit une adresse de service.
 *
 * Theia expose la configuration frontend sur `window.theia`. On tolère son absence pour
 * que les extensions restent testables hors d'une application assemblée.
 */
function readEndpoint(key: string, fallback: string): string {
    const config = (globalThis as Record<string, any>).theia?.frontendConfig;
    return config?.[key] ?? fallback;
}
