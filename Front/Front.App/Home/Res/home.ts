import { Component, computed, inject, signal } from '@angular/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { AuthLevel, CrisError, HttpCrisEndpoint } from '@local/ck-gen';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';
import { PasskeyRegistrationButton } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/Passkey/passkey-registration-button/passkey-registration-button';
import { HelloCommand } from '@local/ck-gen/CK/Sample/Front/HelloCommand';

@Component( {
  selector: 'sample-home',
  imports: [NzAlertModule, NzButtonModule, NzCardModule, PasskeyRegistrationButton],
  templateUrl: './home.html'
} )
export class Home {
  readonly #auth = inject( NgAuthService );
  readonly #cris = inject( HttpCrisEndpoint );

  // The unsafe actor: the actor of the private page, or the weak actor on the public page.
  protected readonly actor = computed( () => this.#auth.authenticationInfo().unsafeActor );
  protected readonly level = computed( () => AuthLevel[this.actor().level] );
  protected readonly isUnsafe = computed( () => this.actor().level === AuthLevel.Unsafe );
  protected readonly answer = signal( '' );
  protected readonly error = signal( '' );
  // The name of the last passkey registered here.
  protected readonly registered = signal( '' );

  async hello(): Promise<void> {
    this.answer.set( '' );
    this.error.set( '' );
    const r = ( await this.#cris.sendAsync( new HelloCommand() ) ).result;
    if( r instanceof CrisError ) this.error.set( r.errors.join( ' ' ) );
    else this.answer.set( r ?? '' );
  }
}
