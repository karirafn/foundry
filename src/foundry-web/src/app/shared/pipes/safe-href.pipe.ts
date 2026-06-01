import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'safeHref',
  standalone: true,
  pure: true,
})
export class SafeHrefPipe implements PipeTransform {
  transform(url: string | null | undefined): string | null {
    if (!url) {
      return null;
    }

    return url.startsWith('https://') ? url : null;
  }
}
