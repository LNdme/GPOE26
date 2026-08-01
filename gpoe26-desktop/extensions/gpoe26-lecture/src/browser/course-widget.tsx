import { Message, ReactWidget } from '@theia/core/lib/browser';
import { inject, injectable, postConstruct } from '@theia/core/shared/inversify';
// `export =` côté Theia : avec esModuleInterop, c'est l'import par défaut qui convient.
import React from '@theia/core/shared/react';
import { HarnessClient } from 'gpoe26-core/lib/browser/harness-client';
import { LocalStore } from 'gpoe26-core/lib/common/store-protocol';
import { RenderedCourseDto, StudyActivityKind } from 'gpoe26-core/lib/common/harness-protocol';

/**
 * Le canvas de lecture.
 *
 * Le HTML vient du harness, déjà rendu : c'est le même que le Web affiche, avec les mêmes
 * ancres. La feuille de style aussi — `reading.css` ne dépendait déjà de rien, elle est
 * reprise telle quelle. Ce qui est écrit ici, c'est ce qui n'existait pas encore : la
 * lecture depuis le disque quand le réseau manque.
 *
 * ⚠️ Le suivi du défilement et le battement de présence restent entièrement dans cette
 * page. Sur le Web, la contrainte venait du circuit SignalR ; ici il n'y a pas de circuit,
 * mais la règle vaut toujours — un signal par frappe ou par pixel défilé rendrait
 * l'application inutilisable sur une connexion scolaire, et inutilement bavarde hors ligne.
 */
@injectable()
export class CourseWidget extends ReactWidget {

    static readonly ID = 'gpoe26.course';

    /** Un signal de présence par minute, comme sur le Web. */
    static readonly HeartbeatInterval = 60_000;

    /** Au-delà, l'étape de lecture est considérée comme faite. */
    static readonly ReadThreshold = 0.9;

    @inject(HarnessClient) protected readonly harness!: HarnessClient;
    @inject(LocalStore) protected readonly store!: LocalStore;

    protected course: RenderedCourseDto | undefined;
    protected courseId: string | undefined;
    protected loading = false;
    protected fromCache = false;

    protected heartbeat: ReturnType<typeof setInterval> | undefined;
    protected scroller: HTMLDivElement | null = null;

    @postConstruct()
    protected init(): void {
        this.id = CourseWidget.ID;
        this.title.label = 'Cours';
        this.title.caption = 'Lecture du cours';
        this.title.iconClass = 'codicon codicon-book';
        this.title.closable = true;

        this.addClass('gpoe26-course');
        this.update();
    }

    async open(courseId: string): Promise<void> {
        this.courseId = courseId;
        this.loading = true;
        this.update();

        const online = this.harness.online;
        this.course = await this.harness.getCourse(courseId);

        // « Depuis le disque » se dit à l'élève : sans cela, il ne saurait pas pourquoi
        // son cours n'a pas la dernière correction de son professeur.
        this.fromCache = !this.harness.online || !online;
        this.loading = false;

        if (this.course) {
            this.title.label = this.course.title;

            const cached = await this.store.getCourse(courseId);
            this.restoreProgress(cached?.readProgress ?? 0);
        }

        this.update();
    }

    // ── Présence ─────────────────────────────────────────────────────────────

    protected override onAfterAttach(message: Message): void {
        super.onAfterAttach(message);

        // Le battement ne compte que si la fenêtre est visible : une application laissée
        // ouverte la nuit ne doit pas produire huit heures de révision. C'est la même
        // règle que sur le Web, et le serveur la fait respecter de toute façon en
        // plafonnant le crédit entre deux signaux.
        this.heartbeat = setInterval(() => {
            if (document.visibilityState !== 'visible' || !this.courseId) return;

            void this.harness.recordActivity({
                courseId: this.courseId,
                kind: StudyActivityKind.Lecture
            });
        }, CourseWidget.HeartbeatInterval);
    }

    protected override onBeforeDetach(message: Message): void {
        if (this.heartbeat) clearInterval(this.heartbeat);
        this.heartbeat = undefined;

        super.onBeforeDetach(message);
    }

    // ── Défilement ───────────────────────────────────────────────────────────

    protected onScroll = (): void => {
        if (!this.scroller || !this.courseId) return;

        const { scrollTop, scrollHeight, clientHeight } = this.scroller;
        const scrollable = scrollHeight - clientHeight;

        // Un cours plus court que la fenêtre n'a rien à défiler : il est lu dès qu'il est
        // ouvert, et le traiter autrement bloquerait l'élève sur une page qu'il a lue.
        const progress = scrollable <= 0 ? 1 : Math.min(1, scrollTop / scrollable);

        void this.store.setReadProgress(this.courseId, progress);
    };

    protected restoreProgress(progress: number): void {
        if (progress <= 0 || progress >= CourseWidget.ReadThreshold) return;

        // Après le rendu, sinon la hauteur du contenu n'est pas encore connue.
        requestAnimationFrame(() => {
            if (!this.scroller) return;

            const scrollable = this.scroller.scrollHeight - this.scroller.clientHeight;
            this.scroller.scrollTop = scrollable * progress;
        });
    }

    // ── Rendu ────────────────────────────────────────────────────────────────

    protected render(): React.ReactNode {
        if (this.loading) {
            return <div className="gpoe26-state">Ouverture du cours…</div>;
        }

        if (!this.course) {
            return (
                <div className="gpoe26-state">
                    <p>Ce cours n'est pas disponible.</p>
                    <p className="gpoe26-state-hint">
                        Connectez-vous une fois pour le télécharger : il restera lisible ensuite,
                        même sans réseau.
                    </p>
                </div>
            );
        }

        return (
            <div className="reading-root gpoe26-reading">
                {this.fromCache && (
                    <div className="gpoe26-offline-banner">
                        Lecture hors ligne — ce cours vient de votre appareil.
                    </div>
                )}

                <header className="gpoe26-course-head">
                    <span className="gpoe26-course-subject">{this.course.subject}</span>
                    <h1 className="gpoe26-course-title">{this.course.title}</h1>
                </header>

                <div className="gpoe26-course-body">
                    {this.course.outline.length > 0 && (
                        <nav className="gpoe26-outline">
                            <p className="gpoe26-outline-title">Sommaire</p>
                            {this.course.outline.map(entry => (
                                <a
                                    key={entry.anchor}
                                    href={`#${entry.anchor}`}
                                    className={`gpoe26-outline-link level-${entry.level}`}
                                    onClick={event => this.scrollTo(event, entry.anchor)}
                                >
                                    {entry.title}
                                </a>
                            ))}
                        </nav>
                    )}

                    <div
                        className="course-canvas"
                        ref={element => { this.scroller = element; }}
                        onScroll={this.onScroll}
                        // Le HTML vient du harness, produit par Markdig avec DisableHtml() :
                        // le balisage brut d'un cours y est échappé, pas exécuté.
                        dangerouslySetInnerHTML={{ __html: this.course.html }}
                    />
                </div>
            </div>
        );
    }

    protected scrollTo(event: React.MouseEvent, anchor: string): void {
        event.preventDefault();

        // Le défilement se fait dans le conteneur, pas dans la page : laisser le
        // navigateur suivre l'ancre déplacerait toute la fenêtre de l'atelier.
        this.scroller?.querySelector(`#${CSS.escape(anchor)}`)
            ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
}
