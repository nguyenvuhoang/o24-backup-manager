/** Cancels superseded work so an old connection cannot publish a result for a new selection. */
export class LatestRequest {
  private current: AbortController | null = null;
  start() {
    this.cancel();
    const request = new AbortController(); this.current = request;
    return request;
  }
  cancel() { this.current?.abort(); this.current = null; }
}
