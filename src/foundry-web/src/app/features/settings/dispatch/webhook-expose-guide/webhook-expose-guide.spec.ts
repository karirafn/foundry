import { TestBed } from '@angular/core/testing';
import { WebhookExposeGuideComponent } from './webhook-expose-guide';

describe('WebhookExposeGuideComponent', () => {
  function render(port: number): HTMLElement {
    const fixture = TestBed.createComponent(WebhookExposeGuideComponent);
    fixture.componentRef.setInput('port', port);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists one command or route per option for the port', () => {
    // Act
    const el = render(6001);

    // Assert
    const terms = Array.from(el.querySelectorAll('dt')).map(dt => dt.textContent?.trim());
    const details = Array.from(el.querySelectorAll('dd')).map(dd => dd.textContent?.trim());
    expect(terms).toEqual(['Cloudflare Tunnel', 'ngrok', 'Tailscale Funnel', 'Reverse proxy (Caddy, nginx, Traefik)']);
    expect(details).toEqual([
      'cloudflared tunnel --url http://localhost:6001',
      'ngrok http 6001',
      'tailscale funnel --bg 6001',
      'Route https://hooks.example.com/webhooks/* to http://<foundry-host>:6001',
    ]);
  });

  it('notes to forward only the webhook port and that quick tunnels change address', () => {
    // Act
    const el = render(6001);

    // Assert
    const notes = Array.from(el.querySelectorAll('.webhook-expose-guide__note')).map(p => p.textContent?.trim());
    expect(notes).toEqual([
      'Forward only port 6001. Foundry\'s main port has no authentication — keep it private.',
      'A quick tunnel gets a new address each time it starts. For a lasting setup, use a named tunnel or a fixed domain.',
    ]);
  });
});
