create <html> transformer
begin
    insert before * """
                    <ck-backoffice-layout
                        [navigationItems]="navigationItems()"
                        [showGlobalSearchBtn]="true"
                        [globalSearchPlaceholder]="'Search.Placeholder' | translate"
                        [displayWCSDropdown]="false"
                        (logoClicked)="goToHome()">
                        <!-- <h1>This is a sample for the CK.Ng.Admin package.</h1> -->

                    """;

    insert """
           </ck-backoffice-layout>

           """ after *;
end

create <ts> transformer
begin
    ensure import { inject, computed, Signal } from '@angular/core';
    ensure import { Layout } from '@local/ck-gen';
    ensure import { NavigationSection } from '@local/ck-gen';
    ensure import { UserService } from '@local/ck-gen';
    ensure import { TranslateModule, TranslateService } from '@ngx-translate/core';
    ensure import { Router } from '@angular/router';

    in after "@Component"
        in first {^braces}
            in after "imports:"
                in first {^[]}
                    replace "RouterOutlet" with "RouterOutlet, Layout, TranslateModule";

    inject """
           readonly #router = inject( Router );
           readonly #userService = inject( UserService );
           readonly #translateService = inject( TranslateService );

           """ into <PreDependencyInjection>;

    inject """
           // The sidebar is data-driven: each admin-only feature contributes a
           // NavigationSection only when the signed-in user is an administrator.
           // isAdmin() is the computed signal added by CK.Ng.Admin on UserService.
           readonly navigationItems: Signal<Array<NavigationSection>> = computed( () => {
             const sections: Array<NavigationSection> = [];
             if ( this.#userService.isAdmin() ) {
               sections.push( {
                 sectionHeadline: this.#translateService.instant( 'CK.Admin.SideBar.Label' ),
                 bottom: false,
                 items: [
                   { label: this.#translateService.instant( 'CK.Admin.UserManagement.Tab.Users' ), routerLink: 'admin/user' }
                 ]
               } );
             }
             return sections;
           } );

           goToHome(): void {
             this.#router.navigate( [''] );
           }
           """ into <PostLocalVariables>;
end
