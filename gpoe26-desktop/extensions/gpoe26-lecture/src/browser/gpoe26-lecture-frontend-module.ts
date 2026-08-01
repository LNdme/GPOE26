import { WidgetFactory } from '@theia/core/lib/browser';
import { ContainerModule } from '@theia/core/shared/inversify';
import { CourseWidget } from './course-widget';

import '../../src/browser/style/index.css';

/**
 * Le canvas de lecture, contribué au shell.
 *
 * C'est une extension Theia — dans le processus, avec accès à l'injection de dépendances.
 * Les types d'exercice, eux, seront des plugins isolés : la frontière passe entre ce que
 * nous écrivons et ce qui peut planter sans emporter l'atelier.
 */
export default new ContainerModule(bind => {
    bind(CourseWidget).toSelf();

    bind(WidgetFactory).toDynamicValue(({ container }) => ({
        id: CourseWidget.ID,
        createWidget: () => container.get(CourseWidget)
    })).inSingletonScope();
});
