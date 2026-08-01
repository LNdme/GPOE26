import { createHmac, timingSafeEqual } from 'node:crypto';

/**
 * L'identité lue dans le JWT, et la règle « qui peut voir les données de qui ».
 *
 * Miroir exact de `GPOE26.ServiceDefaults/StudyIdentity.cs`. La duplication est le prix
 * d'avoir deux implémentations indépendantes ; c'est la suite de conformité qui vérifie
 * qu'elles décident pareil, et son test d'étanchéité est là pour ça.
 */
export interface Identity {
    userId: string;
    role?: string;
    children: string[];
}

/**
 * Vérifie la signature et lit les claims.
 *
 * On valide la signature nous-mêmes plutôt que d'ajouter une bibliothèque : l'algorithme
 * est HS256, la clé est partagée, et une dépendance de moins sur un chemin
 * d'authentification est une surface d'attaque de moins.
 */
export function verify(authorization: string | undefined, secret: string): Identity | undefined {
    const token = authorization?.startsWith('Bearer ') ? authorization.slice(7) : undefined;
    if (!token) return undefined;

    const parts = token.split('.');
    if (parts.length !== 3) return undefined;

    const [header, payload, signature] = parts as [string, string, string];

    const expected = createHmac('sha256', secret)
        .update(`${header}.${payload}`)
        .digest('base64url');

    // Comparaison à temps constant : une comparaison ordinaire laisse fuir, par sa durée,
    // combien de caractères du début sont justes.
    const given = Buffer.from(signature);
    const wanted = Buffer.from(expected);
    if (given.length !== wanted.length || !timingSafeEqual(given, wanted)) return undefined;

    let claims: Record<string, unknown>;
    try {
        claims = JSON.parse(Buffer.from(payload, 'base64url').toString('utf8'));
    } catch {
        return undefined;
    }

    // L'expiration se vérifie ici : une signature valable sur un jeton périmé reste une
    // signature valable.
    const exp = Number(claims.exp);
    if (Number.isFinite(exp) && exp * 1000 < Date.now()) return undefined;

    const userId = String(claims.sub ?? claims.nameid ?? '');
    if (!userId) return undefined;

    return {
        userId,
        role: typeof claims.role === 'string' ? claims.role : undefined,
        children: String(claims.children ?? '')
            .split(',')
            .map(c => c.trim())
            .filter(Boolean),
    };
}

/**
 * L'appelant peut-il consulter les données de cet élève ?
 *
 * Vrai pour l'élève lui-même, et pour un parent auquel il s'est rattaché. Toute autre
 * situation est un refus — y compris un parent qui demanderait l'enfant d'un autre, qui
 * est le cas que le test d'étanchéité vérifie.
 */
export function canViewStudent(identity: Identity, studentId: string): boolean {
    if (identity.userId === studentId) return true;

    const parentOrTeacher = identity.role?.toLowerCase() === 'parent'
        || identity.role?.toLowerCase() === 'teacher';

    return parentOrTeacher && identity.children.includes(studentId);
}
