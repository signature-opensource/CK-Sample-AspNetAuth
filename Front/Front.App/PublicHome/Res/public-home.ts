import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzResultModule } from 'ng-zorro-antd/result';
import { Home } from '@local/ck-gen/CK/Sample/Front/home/home';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';

@Component( {
  selector: 'sample-public-home',
  imports: [RouterLink, NzButtonModule, NzResultModule, Home],
  templateUrl: './public-home.html'
} )
export class PublicHome {
  readonly #auth = inject( NgAuthService );

  // A weak actor (or a remembered user whose session requires a new login) is known but not authenticated.
  protected readonly isKnown = computed( () => this.#auth.authenticationInfo().unsafeActor.actorId !== 0 );
}
