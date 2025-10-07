#import <Cocoa/Cocoa.h>
#include <stdbool.h>

bool isKeyWindowInFullScreen() {
    @autoreleasepool {
        NSWindow *keyWindow = [NSApp keyWindow];
        if (!keyWindow) {
            return false;
        }
        NSWindowStyleMask style = [keyWindow styleMask];
        return (style & NSWindowStyleMaskFullScreen) != 0;
    }
}
