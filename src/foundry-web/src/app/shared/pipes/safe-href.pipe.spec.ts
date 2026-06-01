import { SafeHrefPipe } from './safe-href.pipe';

describe('SafeHrefPipe', () => {
  let pipe: SafeHrefPipe;

  beforeEach(() => {
    pipe = new SafeHrefPipe();
  });

  it('should return the URL when it starts with https://', () => {
    // Arrange / Act
    const result = pipe.transform('https://github.com/owner/repo/issues/1');

    // Assert
    expect(result).toBe('https://github.com/owner/repo/issues/1');
  });

  it('should return null for http:// URLs to prevent non-https links', () => {
    // Arrange / Act
    const result = pipe.transform('http://example.com/path');

    // Assert
    expect(result).toBeNull();
  });

  it('should return null for javascript: URLs', () => {
    // Arrange / Act
    const result = pipe.transform('javascript:alert(1)');

    // Assert
    expect(result).toBeNull();
  });

  it('should return null for data: URLs', () => {
    // Arrange / Act
    const result = pipe.transform('data:text/html,<h1>xss</h1>');

    // Assert
    expect(result).toBeNull();
  });

  it('should return null for null input', () => {
    // Arrange / Act
    const result = pipe.transform(null);

    // Assert
    expect(result).toBeNull();
  });

  it('should return null for undefined input', () => {
    // Arrange / Act
    const result = pipe.transform(undefined);

    // Assert
    expect(result).toBeNull();
  });

  it('should return null for empty string', () => {
    // Arrange / Act
    const result = pipe.transform('');

    // Assert
    expect(result).toBeNull();
  });
});
