create <html> transformer
begin
    replace "<router-outlet />" with """
                                     <ck-backoffice-layout [navigationItems]="navigation">
                                         <router-outlet />
                                     </ck-backoffice-layout>
                                     """;
end

create <ts> transformer
begin
    ensure import { Layout, NavigationSection } from '@local/ck-gen';
    ensure import { faHome, faSitemap } from '@fortawesome/free-solid-svg-icons';

    in after "@Component"
        in first {^braces}
            in after "imports:"
                in first {^[]}
                    replace "RouterOutlet" with "RouterOutlet, Layout";

    inject """
           protected navigation: Array<NavigationSection> = [
               { sectionHeadline: '', bottom: false, items: [{ label: 'Home', routerLink: '/', icon: faHome },
                                                             { label: 'Architecture', routerLink: '/architecture', icon: faSitemap }] }
           ];
           """ into <PostLocalVariables>;
end
