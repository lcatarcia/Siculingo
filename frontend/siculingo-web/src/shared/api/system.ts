import type { paths } from './schema'

type StatusResponse = paths['/api/v1/system/status']['get']['responses'][200]['content']['application/json']
type HealthResponse = paths['/health/ready']['get']['responses'][200]['content']['application/json']

export async function getSystemStatus(signal: AbortSignal): Promise<StatusResponse> {
  const response = await fetch('/api/v1/system/status', { signal })
  if (!response.ok) throw new Error(`API non disponibile (HTTP ${response.status}).`)
  const data: unknown = await response.json()
  if (typeof data !== 'object' || data === null
    || !('service' in data) || typeof data.service !== 'string'
    || !('apiVersion' in data) || typeof data.apiVersion !== 'string') {
    throw new Error('La risposta del servizio non rispetta il contratto OpenAPI.')
  }
  return { service: data.service, apiVersion: data.apiVersion }
}

export async function getReadiness(signal: AbortSignal): Promise<HealthResponse> {
  const response = await fetch('/health/ready', { signal })
  if (!response.ok) {
    throw new Error(response.status === 503
      ? 'Database non pronto: controlla connessione e migrazioni nei log API.'
      : `Verifica del database fallita (HTTP ${response.status}).`)
  }
  const data: unknown = await response.json()
  if (typeof data !== 'object' || data === null || !('status' in data) || data.status !== 'Healthy') {
    throw new Error('Il servizio non ha confermato la disponibilità del database.')
  }
  return { status: data.status }
}
