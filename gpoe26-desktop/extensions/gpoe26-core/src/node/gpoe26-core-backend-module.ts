import { ConnectionHandler, RpcConnectionHandler } from '@theia/core/lib/common';
import { ContainerModule } from '@theia/core/shared/inversify';
import { LOCAL_STORE_PATH, LocalStore } from '../common/store-protocol';
import { FileLocalStore } from './file-local-store';

/**
 * Le magasin local vit dans le backend.
 *
 * C'est le seul endroit qui survit au rechargement de la fenêtre et qui peut écrire sur
 * le disque de l'élève. Le frontend l'atteint par JSON-RPC, comme tout service Theia :
 * l'interface ne manipule jamais de fichiers directement.
 */
export default new ContainerModule(bind => {
    bind(FileLocalStore).toSelf().inSingletonScope();
    bind(LocalStore).toService(FileLocalStore);

    bind(ConnectionHandler)
        .toDynamicValue(({ container }) =>
            new RpcConnectionHandler(LOCAL_STORE_PATH, () => container.get<LocalStore>(LocalStore)))
        .inSingletonScope();
});
