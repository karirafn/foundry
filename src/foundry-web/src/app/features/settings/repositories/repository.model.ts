export interface RepositorySummary {
  id: string;
  slug: string;
  accountId: string;
  accountName: string;
  pollIntervalSeconds: number | null;
  isActive: boolean;
  lastPolledAt: string | null;
}

export interface AvailableRepository {
  slug: string;
  isPrivate: boolean;
}

export interface CreateRepositoryRequest {
  slug: string;
  pollIntervalSeconds: number | null;
}

export interface UpdateRepositoryRequest {
  pollIntervalSeconds: number | null;
  isActive: boolean;
}
