import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Ways to expose Foundry's webhook port on a public https address (ADR 0116): one command or route per
 * tunnel or proxy, for the given port.
 */
@Component({
  selector: 'fd-webhook-expose-guide',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dl class="webhook-expose-guide__list">
      <dt>Cloudflare Tunnel</dt>
      <dd><code>cloudflared tunnel --url http://localhost:{{ port() }}</code></dd>
      <dt>ngrok</dt>
      <dd><code>ngrok http {{ port() }}</code></dd>
      <dt>Tailscale Funnel</dt>
      <dd><code>tailscale funnel --bg {{ port() }}</code></dd>
      <dt>Reverse proxy (Caddy, nginx, Traefik)</dt>
      <dd>Route <code>https://hooks.example.com/webhooks/*</code> to <code>http://&lt;foundry-host&gt;:{{ port() }}</code></dd>
    </dl>
    <p class="webhook-expose-guide__note">Forward only port {{ port() }}. Foundry's main port has no authentication — keep it private.</p>
    <p class="webhook-expose-guide__note">A quick tunnel gets a new address each time it starts. For a lasting setup, use a named tunnel or a fixed domain.</p>
  `,
  styleUrl: './webhook-expose-guide.scss',
})
export class WebhookExposeGuideComponent {
  readonly port = input.required<number>();
}
